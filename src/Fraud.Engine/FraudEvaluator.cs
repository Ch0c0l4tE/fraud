using Fraud.Sdk.Contracts;

namespace Fraud.Engine;

/// <summary>
/// Main fraud evaluator combining rules and ML
/// </summary>
public class FraudEvaluator : IFraudEvaluator
{
    private readonly IRuleEngine _ruleEngine;
    private readonly IMLScorer? _mlScorer;
    private readonly string _modelVersion;

    public FraudEvaluator(IRuleEngine ruleEngine, IMLScorer? mlScorer = null, string modelVersion = "1.0.0")
    {
        _ruleEngine = ruleEngine;
        _mlScorer = mlScorer;
        _modelVersion = modelVersion;
    }

    public async Task<FraudAnalysis> EvaluateAsync(Session session, IReadOnlyList<Signal> signals, CancellationToken cancellationToken = default)
    {
        var riskFactors = new List<RiskFactor>();
        
        // Run rule engine
        var ruleResults = await _ruleEngine.EvaluateAsync(signals, cancellationToken);
        riskFactors.AddRange(ruleResults);
        
        // Run ML scorer if available
        if (_mlScorer != null)
        {
            var mlResults = await _mlScorer.ScoreAsync(signals, cancellationToken);
            riskFactors.AddRange(mlResults);
        }
        
        // Calculate weighted score
        var totalWeight = riskFactors.Sum(r => r.Weight);
        var weightedScore = totalWeight > 0 
            ? riskFactors.Sum(r => r.Score * r.Weight) / totalWeight 
            : 0.0;
        
        // Determine verdict
        var verdict = weightedScore switch
        {
            < 0.3 => FraudVerdict.Allow,
            < 0.7 => FraudVerdict.Review,
            _ => FraudVerdict.Block
        };
        
        return new FraudAnalysis
        {
            SessionId = session.Id,
            Verdict = verdict,
            ConfidenceScore = weightedScore,
            RiskFactors = riskFactors,
            ModelVersion = _modelVersion,
            EvaluatedAt = DateTimeOffset.UtcNow
        };
    }
}
