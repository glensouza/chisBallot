-- chisBallot schema. See docs/data-model.md for what each table is for.
-- Batches are separated by lines containing only GO.

CREATE TABLE dbo.Elections (
    Id              uniqueidentifier NOT NULL PRIMARY KEY,
    Slug            nvarchar(60)     NOT NULL UNIQUE,
    Name            nvarchar(200)    NOT NULL,
    Status          tinyint          NOT NULL CHECK (Status IN (0, 1, 2)), -- 0 Draft, 1 Open, 2 Closed
    CodesIssued     int              NOT NULL DEFAULT 0,
    ShowLiveResults bit              NOT NULL DEFAULT 0,
    ShuffleContests bit              NOT NULL DEFAULT 0,
    ShuffleChoices  bit              NOT NULL DEFAULT 0,
    IsDemo          bit              NOT NULL DEFAULT 0
);
GO

CREATE TABLE dbo.Contests (
    Id            uniqueidentifier NOT NULL PRIMARY KEY,
    ElectionId    uniqueidentifier NOT NULL REFERENCES dbo.Elections (Id),
    Title         nvarchar(200)    NOT NULL,
    Seats         int              NOT NULL DEFAULT 1 CHECK (Seats >= 1),
    MaxSelections int              NOT NULL DEFAULT 1 CHECK (MaxSelections >= 1),
    AllowWriteIn  bit              NOT NULL DEFAULT 0,
    SortOrder     int              NOT NULL
);
GO

CREATE TABLE dbo.Choices (
    Id        uniqueidentifier NOT NULL PRIMARY KEY,
    ContestId uniqueidentifier NOT NULL REFERENCES dbo.Contests (Id),
    Label     nvarchar(200)    NOT NULL,
    SortOrder int              NOT NULL
);
GO

-- One row per printed slip. Only a hash of the code is stored.
CREATE TABLE dbo.VotingCodes (
    ElectionId uniqueidentifier NOT NULL REFERENCES dbo.Elections (Id),
    CodeHash   binary(32)       NOT NULL,
    Used       bit              NOT NULL DEFAULT 0,
    PRIMARY KEY (ElectionId, CodeHash)
);
GO

-- No voting code, no voter, no timestamp, no sequence number.
CREATE TABLE dbo.Ballots (
    Id          uniqueidentifier NOT NULL PRIMARY KEY, -- random, never sequential
    ElectionId  uniqueidentifier NOT NULL REFERENCES dbo.Elections (Id),
    ReceiptCode char(11)         NOT NULL,
    UNIQUE (ElectionId, ReceiptCode)
);
GO

CREATE TABLE dbo.BallotSelections (
    BallotId  uniqueidentifier NOT NULL REFERENCES dbo.Ballots (Id),
    ContestId uniqueidentifier NOT NULL REFERENCES dbo.Contests (Id),
    ChoiceId  uniqueidentifier NOT NULL REFERENCES dbo.Choices (Id),
    PRIMARY KEY (BallotId, ContestId, ChoiceId)
);
GO

CREATE TABLE dbo.BallotWriteIns (
    BallotId  uniqueidentifier NOT NULL REFERENCES dbo.Ballots (Id),
    ContestId uniqueidentifier NOT NULL REFERENCES dbo.Contests (Id),
    Name      nvarchar(60)     NOT NULL,
    NameKey   nvarchar(60)     NOT NULL,
    PRIMARY KEY (BallotId, ContestId, NameKey)
);
GO

CREATE TYPE dbo.SelectionList AS TABLE (
    ContestId uniqueidentifier NOT NULL,
    ChoiceId  uniqueidentifier NOT NULL,
    PRIMARY KEY (ContestId, ChoiceId)
);
GO

CREATE TYPE dbo.WriteInList AS TABLE (
    ContestId uniqueidentifier NOT NULL,
    Name      nvarchar(60)     NOT NULL,
    NameKey   nvarchar(60)     NOT NULL,
    PRIMARY KEY (ContestId, NameKey)
);
GO

-- Election lifecycle: Draft -> Open -> Closed, and Closed is final. Once an
-- election is Open, the settings that affect how it is run are frozen.
CREATE TRIGGER dbo.TR_Elections_Lifecycle ON dbo.Elections AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
        FROM deleted d
        JOIN inserted i ON i.Id = d.Id
        WHERE d.Status = 2
           OR (d.Status = 0 AND i.Status NOT IN (0, 1))
           OR (d.Status = 1 AND i.Status NOT IN (1, 2))
           OR i.IsDemo <> d.IsDemo
           OR (d.Status <> 0 AND (   i.Slug <> d.Slug
                                  OR i.CodesIssued <> d.CodesIssued
                                  OR i.ShowLiveResults <> d.ShowLiveResults
                                  OR i.ShuffleContests <> d.ShuffleContests
                                  OR i.ShuffleChoices <> d.ShuffleChoices)))
    BEGIN
        THROW 50002, 'This change to the election is not allowed.', 1;
    END
END;
GO

-- Casting a ballot is one transaction: consume the code, insert the ballot.
-- @Result: 0 cast, 1 code refused (unknown, used, wrong election or not open),
-- 2 selections refused. Nothing records which code produced which ballot.
CREATE PROCEDURE dbo.CastBallot
    @ElectionId  uniqueidentifier,
    @CodeHash    binary(32),
    @ReceiptCode char(11),
    @Selections  dbo.SelectionList READONLY,
    @WriteIns    dbo.WriteInList READONLY,
    @Result      int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    IF EXISTS (
            SELECT 1
            FROM @Selections s
            LEFT JOIN dbo.Choices ch ON ch.Id = s.ChoiceId AND ch.ContestId = s.ContestId
            LEFT JOIN dbo.Contests c ON c.Id = s.ContestId AND c.ElectionId = @ElectionId
            WHERE ch.Id IS NULL OR c.Id IS NULL)
        OR EXISTS (
            SELECT 1
            FROM @WriteIns w
            LEFT JOIN dbo.Contests c ON c.Id = w.ContestId AND c.ElectionId = @ElectionId AND c.AllowWriteIn = 1
            WHERE c.Id IS NULL)
        OR EXISTS (
            SELECT 1
            FROM dbo.Contests c
            WHERE c.ElectionId = @ElectionId
              AND (SELECT COUNT(*) FROM @Selections s WHERE s.ContestId = c.Id)
                + (SELECT COUNT(*) FROM @WriteIns w WHERE w.ContestId = c.Id) > c.MaxSelections)
    BEGIN
        ROLLBACK TRANSACTION;
        SET @Result = 2;
        RETURN;
    END

    UPDATE vc
    SET Used = 1
    FROM dbo.VotingCodes vc
    JOIN dbo.Elections e ON e.Id = vc.ElectionId
    WHERE vc.ElectionId = @ElectionId
      AND vc.CodeHash = @CodeHash
      AND vc.Used = 0
      AND e.Status = 1;

    IF @@ROWCOUNT = 0
    BEGIN
        ROLLBACK TRANSACTION;
        SET @Result = 1;
        RETURN;
    END

    DECLARE @BallotId uniqueidentifier = NEWID();

    INSERT dbo.Ballots (Id, ElectionId, ReceiptCode)
    VALUES (@BallotId, @ElectionId, @ReceiptCode);

    INSERT dbo.BallotSelections (BallotId, ContestId, ChoiceId)
    SELECT @BallotId, ContestId, ChoiceId FROM @Selections;

    INSERT dbo.BallotWriteIns (BallotId, ContestId, Name, NameKey)
    SELECT @BallotId, ContestId, Name, NameKey FROM @WriteIns;

    COMMIT TRANSACTION;
    SET @Result = 0;
END;
GO

-- Turnout is always public: how many slips were used, never what was voted.
CREATE PROCEDURE dbo.GetTurnout
    @ElectionId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        e.CodesIssued,
        (SELECT COUNT(*) FROM dbo.VotingCodes vc WHERE vc.ElectionId = e.Id AND vc.Used = 1) AS CodesUsed,
        (SELECT COUNT(*) FROM dbo.Ballots b WHERE b.ElectionId = e.Id) AS BallotsCounted
    FROM dbo.Elections e
    WHERE e.Id = @ElectionId;
END;
GO

-- Results are readable only once the election is Closed, or while Open when
-- the election turned on live results. Live results are per-choice counts;
-- write-ins appear as one combined count until Closed.
CREATE PROCEDURE dbo.GetResults
    @ElectionId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Status tinyint;
    DECLARE @Live bit;
    SELECT @Status = Status, @Live = ShowLiveResults FROM dbo.Elections WHERE Id = @ElectionId;

    IF @Status IS NULL OR NOT (@Status = 2 OR (@Status = 1 AND @Live = 1))
    BEGIN
        THROW 50001, 'Results are not available.', 1;
    END

    SELECT ContestId, ChoiceId, WriteIn, Votes
    FROM (
        SELECT ch.ContestId,
               ch.Id AS ChoiceId,
               CAST(NULL AS nvarchar(60)) AS WriteIn,
               COUNT(bs.BallotId) AS Votes
        FROM dbo.Choices ch
        JOIN dbo.Contests c ON c.Id = ch.ContestId
        LEFT JOIN dbo.BallotSelections bs ON bs.ChoiceId = ch.Id
        WHERE c.ElectionId = @ElectionId
        GROUP BY ch.ContestId, ch.Id

        UNION ALL

        SELECT w.ContestId,
               CAST(NULL AS uniqueidentifier),
               CASE WHEN @Status = 2 THEN w.NameKey END,
               COUNT(*)
        FROM dbo.BallotWriteIns w
        JOIN dbo.Contests c ON c.Id = w.ContestId
        WHERE c.ElectionId = @ElectionId
        GROUP BY w.ContestId, CASE WHEN @Status = 2 THEN w.NameKey END
    ) r
    ORDER BY ContestId, Votes DESC, ChoiceId;
END;
GO

-- Closing records the final state and deletes the codes nobody used.
CREATE PROCEDURE dbo.CloseElection
    @ElectionId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    UPDATE dbo.Elections SET Status = 2 WHERE Id = @ElectionId AND Status = 1;

    IF @@ROWCOUNT = 0
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50003, 'Only an open election can be closed.', 1;
    END

    DELETE FROM dbo.VotingCodes WHERE ElectionId = @ElectionId AND Used = 0;

    COMMIT TRANSACTION;
END;
GO
