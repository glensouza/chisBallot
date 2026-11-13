namespace ChisBallot.Contracts;

public sealed record CastRequest(Guid ElectionId, string Code, IReadOnlyList<SelectionDto> Selections, IReadOnlyList<WriteInDto> WriteIns, ProofOfWorkAnswer? Proof = null);
