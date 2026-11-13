namespace ChisBallot.Tests;

using ChisBallot.Contracts;

public sealed class ResultsCalculatorTests
{
    private static ContestResult Rank(int seats, params (string Label, int Votes)[] counts)
    {
        return ResultsCalculator.Rank(Guid.NewGuid(), "Question", seats, counts.Select(c => (c.Label, c.Votes, false)));
    }

    private static string[] Winners(ContestResult result) => result.Entries.Where(e => e.IsWinner).Select(e => e.Label).ToArray();

    [Fact]
    public void The_top_choices_win_when_nothing_is_level()
    {
        ContestResult result = Rank(3, ("A", 9), ("B", 7), ("C", 5), ("D", 4), ("E", 1));

        Assert.Equal(["A", "B", "C"], Winners(result));
        Assert.Null(result.Tie);
    }

    [Fact]
    public void A_tie_for_the_last_seat_is_flagged_not_broken()
    {
        ContestResult result = Rank(3, ("A", 9), ("B", 7), ("C", 5), ("D", 5), ("E", 1));

        Assert.Equal(["A", "B"], Winners(result));
        Assert.NotNull(result.Tie);
        Assert.Equal(["C", "D"], result.Tie.TiedLabels);
        Assert.Equal(1, result.Tie.OpenSeats);
    }

    [Fact]
    public void A_three_way_tie_over_two_open_seats_lists_all_three()
    {
        ContestResult result = Rank(3, ("A", 9), ("B", 4), ("C", 4), ("D", 4), ("E", 1));

        Assert.Equal(["A"], Winners(result));
        Assert.Equal(["B", "C", "D"], result.Tie!.TiedLabels);
        Assert.Equal(2, result.Tie.OpenSeats);
    }

    [Fact]
    public void Level_totals_inside_the_winning_places_do_not_matter()
    {
        ContestResult result = Rank(3, ("A", 8), ("B", 8), ("C", 8), ("D", 2));

        Assert.Equal(["A", "B", "C"], Winners(result));
        Assert.Null(result.Tie);
    }

    [Fact]
    public void Level_totals_below_the_winning_places_do_not_matter()
    {
        ContestResult result = Rank(1, ("A", 9), ("B", 2), ("C", 2));

        Assert.Equal(["A"], Winners(result));
        Assert.Null(result.Tie);
    }

    [Fact]
    public void An_equal_yes_no_vote_is_a_tie()
    {
        ContestResult result = Rank(1, ("Yes", 6), ("No", 6));

        Assert.Empty(Winners(result));
        Assert.Equal(["No", "Yes"], result.Tie!.TiedLabels.Order().ToArray());
        Assert.Equal(1, result.Tie.OpenSeats);
    }

    [Fact]
    public void A_vote_nobody_voted_in_is_a_tie()
    {
        ContestResult result = Rank(1, ("Yes", 0), ("No", 0));

        Assert.NotNull(result.Tie);
        Assert.Empty(Winners(result));
    }

    [Fact]
    public void Choices_with_no_votes_that_would_fill_a_seat_are_a_tie()
    {
        ContestResult result = Rank(3, ("A", 5), ("B", 2), ("C", 0), ("D", 0), ("E", 0));

        Assert.Equal(["A", "B"], Winners(result));
        Assert.Equal(["C", "D", "E"], result.Tie!.TiedLabels);
        Assert.Equal(1, result.Tie.OpenSeats);
    }

    [Fact]
    public void Write_ins_are_ranked_like_any_other_choice()
    {
        ContestResult result = ResultsCalculator.Rank(
            Guid.NewGuid(),
            "Question",
            1,
            [("A", 3, false), ("B", 2, false), ("pat lee", 5, true)]);

        ResultEntry winner = Assert.Single(result.Entries, e => e.IsWinner);
        Assert.Equal("pat lee", winner.Label);
        Assert.True(winner.IsWriteIn);
    }

    [Fact]
    public void Entries_are_listed_most_votes_first()
    {
        ContestResult result = Rank(1, ("B", 1), ("A", 3), ("C", 2));

        Assert.Equal(["A", "C", "B"], result.Entries.Select(e => e.Label).ToArray());
    }
}
