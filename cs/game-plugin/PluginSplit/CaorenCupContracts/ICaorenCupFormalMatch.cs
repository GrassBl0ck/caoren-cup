using CaorenCup.Contracts.MatchRuntime;

namespace CaorenCup.Contracts;

public interface ICaorenCupFormalMatch
{
    FormalMatchRuntime FormalMatch { get; }
}
