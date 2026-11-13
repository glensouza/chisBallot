namespace ChisBallot.Tests;

using ChisBallot.Contracts;
using ChisBallot.Data;

public sealed class ProofOfWorkTests
{
    private const int Difficulty = 8;

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => this.Now;
    }

    private static async Task<ProofOfWorkAnswer> SolvedAsync(ProofOfWorkVerifier verifier)
    {
        ProofOfWorkChallenge challenge = verifier.Create();
        long counter = await ProofOfWorkSolver.SolveAsync(challenge.Nonce, challenge.Difficulty);
        return new ProofOfWorkAnswer(challenge.Nonce, challenge.Expires, challenge.Difficulty, challenge.Signature, counter);
    }

    [Fact]
    public async Task A_solved_challenge_is_accepted_once()
    {
        ProofOfWorkVerifier verifier = new(Difficulty, TimeSpan.FromMinutes(5), new Clock());
        ProofOfWorkAnswer answer = await SolvedAsync(verifier);

        Assert.True(verifier.Verify(answer));
        Assert.False(verifier.Verify(answer));
    }

    [Fact]
    public async Task A_wrong_number_is_refused_and_does_not_use_up_the_challenge()
    {
        ProofOfWorkVerifier verifier = new(Difficulty, TimeSpan.FromMinutes(5), new Clock());
        ProofOfWorkAnswer answer = await SolvedAsync(verifier);

        long wrong = answer.Counter;
        while (ProofOfWorkSolver.Check(answer.Nonce, wrong, Difficulty))
        {
            wrong++;
        }

        Assert.False(verifier.Verify(answer with { Counter = wrong }));
        Assert.True(verifier.Verify(answer));
    }

    [Fact]
    public async Task A_challenge_the_site_did_not_make_is_refused()
    {
        ProofOfWorkVerifier verifier = new(Difficulty, TimeSpan.FromMinutes(5), new Clock());
        ProofOfWorkVerifier other = new(Difficulty, TimeSpan.FromMinutes(5), new Clock());
        ProofOfWorkAnswer answer = await SolvedAsync(other);

        Assert.False(verifier.Verify(answer));
    }

    [Fact]
    public async Task A_changed_challenge_is_refused()
    {
        ProofOfWorkVerifier verifier = new(Difficulty, TimeSpan.FromMinutes(5), new Clock());
        ProofOfWorkAnswer answer = await SolvedAsync(verifier);

        Assert.False(verifier.Verify(answer with { Expires = answer.Expires + 3600 }));
        Assert.False(verifier.Verify(answer with { Difficulty = 1 }));
        Assert.False(verifier.Verify(answer with { Signature = "x" }));
        Assert.False(verifier.Verify(null));
    }

    [Fact]
    public async Task An_expired_challenge_is_refused()
    {
        Clock clock = new();
        ProofOfWorkVerifier verifier = new(Difficulty, TimeSpan.FromMinutes(5), clock);
        ProofOfWorkAnswer answer = await SolvedAsync(verifier);

        clock.Now += TimeSpan.FromMinutes(6);

        Assert.False(verifier.Verify(answer));
    }

    [Fact]
    public async Task Solving_takes_longer_as_the_difficulty_goes_up_and_always_meets_it()
    {
        for (int difficulty = 4; difficulty <= 12; difficulty += 4)
        {
            long counter = await ProofOfWorkSolver.SolveAsync("nonce", difficulty);
            Assert.True(ProofOfWorkSolver.Check("nonce", counter, difficulty));
        }
    }
}
