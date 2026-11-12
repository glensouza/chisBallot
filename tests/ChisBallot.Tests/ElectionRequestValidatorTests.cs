using ChisBallot.Contracts;

namespace ChisBallot.Tests;

public sealed class ElectionRequestValidatorTests
{
    private static CreateElectionRequest Valid() => new(
        "Board election",
        "board-election-2026",
        false,
        false,
        false,
        [new ContestInput("Pick three", 3, 3, false, ["A", "B", "C", "D", "E"])]);

    [Fact]
    public void A_sensible_election_is_accepted()
    {
        Assert.Null(ElectionRequestValidator.Validate(Valid()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Has Capitals")]
    [InlineData("has space")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("double--dash")]
    [InlineData("under_score")]
    public void An_unsafe_address_is_refused(string slug)
    {
        Assert.NotNull(ElectionRequestValidator.Validate(Valid() with { Slug = slug }));
    }

    [Fact]
    public void A_blank_name_or_no_questions_is_refused()
    {
        Assert.NotNull(ElectionRequestValidator.Validate(Valid() with { Name = "  " }));
        Assert.NotNull(ElectionRequestValidator.Validate(Valid() with { Contests = [] }));
    }

    [Fact]
    public void A_question_needs_more_choices_than_seats()
    {
        ContestInput everyoneWins = new("Pick three", 3, 3, false, ["A", "B", "C"]);
        ContestInput onlyOne = new("Pick one", 1, 1, false, ["A"]);

        Assert.NotNull(ElectionRequestValidator.Validate(Valid() with { Contests = [everyoneWins] }));
        Assert.NotNull(ElectionRequestValidator.Validate(Valid() with { Contests = [onlyOne] }));
    }

    [Fact]
    public void Voters_must_be_able_to_pick_at_least_as_many_as_there_are_seats_and_no_more_than_exist()
    {
        ContestInput tooFewPicks = new("Pick three", 3, 2, false, ["A", "B", "C", "D"]);
        ContestInput tooManyPicks = new("Pick", 1, 5, false, ["A", "B"]);
        ContestInput writeInRoom = new("Pick", 1, 3, true, ["A", "B"]);

        Assert.NotNull(ElectionRequestValidator.Validate(Valid() with { Contests = [tooFewPicks] }));
        Assert.NotNull(ElectionRequestValidator.Validate(Valid() with { Contests = [tooManyPicks] }));
        Assert.Null(ElectionRequestValidator.Validate(Valid() with { Contests = [writeInRoom] }));
    }

    [Fact]
    public void Blank_or_repeated_choices_are_refused()
    {
        Assert.NotNull(ElectionRequestValidator.Validate(Valid() with { Contests = [new ContestInput("Q", 1, 1, false, ["A", " "])] }));
        Assert.NotNull(ElectionRequestValidator.Validate(Valid() with { Contests = [new ContestInput("Q", 1, 1, false, ["Yes", "yes "])] }));
    }
}
