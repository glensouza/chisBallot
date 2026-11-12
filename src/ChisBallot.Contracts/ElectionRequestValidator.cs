using System.Text.RegularExpressions;

namespace ChisBallot.Contracts;

/// <summary>
/// The rules for a new election. The admin page checks them as you type and the
/// admin API checks them again, so a bad request never reaches the database.
/// </summary>
public static partial class ElectionRequestValidator
{
    public const int MaxNameLength = 200;
    public const int MaxSlugLength = 60;
    public const int MaxContests = 20;
    public const int MaxChoices = 40;

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    /// <summary>Returns the first problem found, or null when the request is acceptable.</summary>
    public static string? Validate(CreateElectionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > MaxNameLength)
        {
            return $"Give the vote a name of up to {MaxNameLength} characters.";
        }

        if (string.IsNullOrEmpty(request.Slug) || request.Slug.Length > MaxSlugLength || !SlugPattern().IsMatch(request.Slug))
        {
            return "The address can only use lower-case letters, numbers and single dashes, like board-election-2026.";
        }

        if (request.Contests is null || request.Contests.Count == 0 || request.Contests.Count > MaxContests)
        {
            return $"Add between 1 and {MaxContests} questions.";
        }

        foreach (ContestInput contest in request.Contests)
        {
            string? problem = ValidateContest(contest);
            if (problem is not null)
            {
                return problem;
            }
        }

        return null;
    }

    private static string? ValidateContest(ContestInput contest)
    {
        if (string.IsNullOrWhiteSpace(contest.Title) || contest.Title.Length > MaxNameLength)
        {
            return $"Every question needs a title of up to {MaxNameLength} characters.";
        }

        if (contest.Choices is null || contest.Choices.Count < 2 || contest.Choices.Count > MaxChoices)
        {
            return $"\"{contest.Title}\" needs between 2 and {MaxChoices} choices.";
        }

        if (contest.Choices.Any(choice => string.IsNullOrWhiteSpace(choice) || choice.Length > MaxNameLength))
        {
            return $"Every choice in \"{contest.Title}\" needs a label of up to {MaxNameLength} characters.";
        }

        if (contest.Choices.Select(choice => choice.Trim().ToLowerInvariant()).Distinct().Count() != contest.Choices.Count)
        {
            return $"\"{contest.Title}\" has the same choice twice.";
        }

        if (contest.Seats < 1 || contest.Seats >= contest.Choices.Count)
        {
            return $"\"{contest.Title}\" needs more choices than seats ({contest.Seats} seats, {contest.Choices.Count} choices).";
        }

        int ceiling = contest.Choices.Count + (contest.AllowWriteIn ? 1 : 0);
        if (contest.MaxSelections < 1 || contest.MaxSelections > ceiling)
        {
            return $"\"{contest.Title}\" lets a voter pick {contest.MaxSelections}, but there are only {ceiling} things to pick.";
        }

        if (contest.MaxSelections < contest.Seats)
        {
            return $"\"{contest.Title}\" has {contest.Seats} seats, so voters must be able to pick at least that many.";
        }

        return null;
    }
}
