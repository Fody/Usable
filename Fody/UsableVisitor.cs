using System;
using System.Collections.Generic;
using System.Linq;
using Anotar.Custom;
using Custom.Decompiler.ILAst;
using Mono.Cecil;

public class UsableVisitor : ILNodeVisitor
{
    private readonly MethodDefinition method;
    private readonly Dictionary<Tuple<ILVariable, int>, int> starts;
    private readonly List<int> currentTrys;
    private int currentScope;

    public UsableVisitor(MethodDefinition method)
    {
        this.method = method;
        UsingRanges = new List<ILRange>();
        EarlyReturns = new List<int>();
        starts = new Dictionary<Tuple<ILVariable, int>, int>();
        currentTrys = method.Body.ExceptionHandlers.Select(handler => handler.TryStart.Offset).ToList();
    }

    public List<ILRange> UsingRanges { get; private set; }
    public List<int> EarlyReturns { get; private set; }

    protected override ILExpression VisitExpression(ILExpression expression)
    {
        if (expression.Code == ILCode.Stloc)
        {
            var variable = (ILVariable)expression.Operand;

            if (variable.Type.HasInterface("System.IDisposable"))
            {
                var key = Tuple.Create(variable, currentScope);

                if (starts.Keys.Any(k => k.Item1 == variable && k.Item2 != currentScope))
                {
                    LogTo.Warning("Method {0}: Using cannot be added because reassigning a variable in a condition is not supported.", method);
                }
                else
                {
                    if (starts.ContainsKey(key))
                    {
                        UsingRanges.Add(new ILRange { From = starts[key], To = expression.FirstILOffset() });
                        starts.Remove(key);
                    }

                    if (!currentTrys.Contains(expression.LastILOffset()))
                        starts.Add(key, expression.LastILOffset());
                }
            }
        }
        if (expression.Code == ILCode.Ret && currentScope > 1)
        {
            EarlyReturns.Add(expression.FirstILOffset());
        }

        return base.VisitExpression(expression);
    }

    protected override ILBlock VisitBlock(ILBlock block)
    {
        currentScope++;

        var result = base.VisitBlock(block);

        currentScope--;

        if (block.Body.Count == 0)
            return result;

        var toOffset = block.LastILOffset();

        if (toOffset < 0)
            return result;

        foreach (var start in starts.Where(kvp => kvp.Key.Item2 == currentScope + 1).ToList())
        {
            starts.Remove(start.Key);

            List<ILExpression> args;
            if (block.Body.Last().Match(ILCode.Ret, out args) && args.Count == 1 && args[0].MatchLdloc(start.Key.Item1))
                continue; // Returning the variable

            UsingRanges.Add(new ILRange { From = start.Value, To = toOffset });
        }

        return result;
    }
}