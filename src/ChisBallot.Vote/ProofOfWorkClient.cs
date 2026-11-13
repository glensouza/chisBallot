namespace ChisBallot.Vote;

using System.Net.Http.Json;
using ChisBallot.Contracts;

/// <summary>
/// Gets a puzzle from the voting site and solves it in the browser, so a ballot or a demo slip
/// can be sent with its proof. Solving takes a moment on a phone; the page says so while it works.
/// </summary>
public sealed class ProofOfWorkClient(HttpClient http)
{
    public async Task<ProofOfWorkAnswer?> SolveAsync(CancellationToken cancellationToken = default)
    {
        ProofOfWorkChallenge? challenge = await http.GetFromJsonAsync<ProofOfWorkChallenge>("api/challenge", cancellationToken);
        if (challenge is null)
        {
            return null;
        }

        long counter = await ProofOfWorkSolver.SolveAsync(challenge.Nonce, challenge.Difficulty, cancellationToken);
        return new ProofOfWorkAnswer(challenge.Nonce, challenge.Expires, challenge.Difficulty, challenge.Signature, counter);
    }
}
