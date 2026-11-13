namespace ChisBallot.Contracts;

/// <summary>Turnout is public: how many slips were used, never what was voted.</summary>
public sealed record TurnoutView(int CodesIssued, int CodesUsed, int BallotsCounted);
