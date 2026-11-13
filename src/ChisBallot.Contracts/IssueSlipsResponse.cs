namespace ChisBallot.Contracts;

/// <summary>
/// The plain codes, returned once and never stored. If the page that receives
/// them is lost, the slips cannot be recovered and the vote must be made again.
/// </summary>
public sealed record IssueSlipsResponse(IReadOnlyList<string>? Codes, string? Error);
