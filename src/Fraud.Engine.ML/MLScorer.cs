using Fraud.Sdk.Contracts;

namespace Fraud.Engine.ML;

/// <summary>
/// Mock ML scorer for development (Phase 3b placeholder)
/// Will be replaced with ONNX/ML.NET integration
/// </summary>
public class MockMLScorer : IMLScorer
{
    private readonly Random _random = new();
    
    public Task<IReadOnlyList<RiskFactor>> ScoreAsync(IReadOnlyList<Signal> signals, CancellationToken cancellationToken = default)
    {
        // Mock implementation - returns random scores for demo
        var results = new List<RiskFactor>();
        
        if (signals.Count > 0)
        {
            // Simulate ML model output
            var anomalyScore = _random.NextDouble() * 0.5; // Bias toward low scores for mock
            
            if (anomalyScore > 0.2)
            {
                results.Add(new RiskFactor
                {
                    Name = "ml_anomaly_score",
                    Score = anomalyScore,
                    Weight = 0.4,
                    Description = "ML model anomaly detection score (MOCK)"
                });
            }
        }
        
        return Task.FromResult<IReadOnlyList<RiskFactor>>(results);
    }
}

/// <summary>
/// ONNX-based ML scorer (to be implemented in Phase 3b)
/// </summary>
public class OnnxMLScorer : IMLScorer
{
    private readonly string _modelPath;
    
    public OnnxMLScorer(string modelPath)
    {
        _modelPath = modelPath;
    }
    
    public Task<IReadOnlyList<RiskFactor>> ScoreAsync(IReadOnlyList<Signal> signals, CancellationToken cancellationToken = default)
    {
        // TODO: Implement ONNX model inference
        // 1. Extract features from signals
        // 2. Load ONNX model from _modelPath
        // 3. Run inference
        // 4. Convert output to RiskFactors
        
        throw new NotImplementedException("ONNX scorer not yet implemented. Use MockMLScorer for development.");
    }
}
