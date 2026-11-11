-- The two database users. The migrator replaces the $(...) placeholders with
-- the passwords from the deployment; they are never stored in this file.
--
--   chisballot_vote   used by the voting site. Can read what a ballot page
--                     needs and run the stored procedures below. It has no
--                     right to read or write ballots, codes or tallies
--                     directly, so results cannot be read early.
--   chisballot_admin  used by the admin site. Can manage elections, contests,
--                     choices and codes, and close an election. It has no
--                     right to read ballots at all.

CREATE USER chisballot_vote WITH PASSWORD = '$(VotePassword)';
GO

CREATE USER chisballot_admin WITH PASSWORD = '$(AdminPassword)';
GO

GRANT SELECT ON dbo.Elections TO chisballot_vote;
GRANT SELECT ON dbo.Contests TO chisballot_vote;
GRANT SELECT ON dbo.Choices TO chisballot_vote;
GRANT EXECUTE ON dbo.CastBallot TO chisballot_vote;
GRANT EXECUTE ON dbo.GetTurnout TO chisballot_vote;
GRANT EXECUTE ON dbo.GetResults TO chisballot_vote;
GRANT EXECUTE ON TYPE::dbo.SelectionList TO chisballot_vote;
GRANT EXECUTE ON TYPE::dbo.WriteInList TO chisballot_vote;
GO

GRANT SELECT, INSERT, UPDATE ON dbo.Elections TO chisballot_admin;
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Contests TO chisballot_admin;
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Choices TO chisballot_admin;
GRANT INSERT ON dbo.VotingCodes TO chisballot_admin;
GRANT EXECUTE ON dbo.GetTurnout TO chisballot_admin;
GRANT EXECUTE ON dbo.GetResults TO chisballot_admin;
GRANT EXECUTE ON dbo.CloseElection TO chisballot_admin;
GO
