using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML;
using Microsoft.ML.Data;
using Takeaway.Api.Options;

namespace Takeaway.Api.VoiceDialog.IntentClassification;

public sealed class MlNetIntentClassifier : IIntentClassifier
{
    private readonly IntentClassifierOptions _options;
    private readonly ILogger<MlNetIntentClassifier> _logger;
    private readonly Lazy<ModelHolder?> _modelHolder;

    public MlNetIntentClassifier(IOptions<IntentClassifierOptions> options, ILogger<MlNetIntentClassifier> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
        _modelHolder = new Lazy<ModelHolder?>(InitializeModel, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IntentPrediction PredictIntent(string? utterance)
    {
        if (!_options.Enabled)
        {
            return IntentPrediction.Disabled;
        }

        if (string.IsNullOrWhiteSpace(utterance))
        {
            return new IntentPrediction(null, 0d, true, false);
        }

        var holder = _modelHolder.Value;
        if (holder is null)
        {
            return new IntentPrediction(null, 0d, false, false);
        }

        try
        {
            using var engine = holder.MlContext.Model.CreatePredictionEngine<ModelInput, ModelOutput>(holder.Model);
            var output = engine.Predict(new ModelInput { Utterance = utterance });
            var (label, confidence) = holder.GetBestLabel(output.Score, output.PredictedLabel);

            if (confidence < _options.MinimumConfidence)
            {
                return new IntentPrediction(null, confidence, true, false);
            }

            return new IntentPrediction(label, confidence, true, !string.IsNullOrWhiteSpace(label));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to classify voice intent.");
            return new IntentPrediction(null, 0d, true, false);
        }
    }

    private ModelHolder? InitializeModel()
    {
        if (!_options.Enabled)
        {
            return null;
        }

        try
        {
            var mlContext = new MLContext(seed: 42);
            ITransformer model;

            if (!string.IsNullOrWhiteSpace(_options.ModelPath) && File.Exists(_options.ModelPath))
            {
                using var stream = File.OpenRead(_options.ModelPath);
                model = mlContext.Model.Load(stream, out _);
                _logger.LogInformation("Loaded intent classification model from {Path}.", _options.ModelPath);
            }
            else
            {
                var trainingData = mlContext.Data.LoadFromEnumerable(CreateDefaultTrainingSet());
                var pipeline = mlContext.Transforms.Conversion.MapValueToKey("Label", nameof(IntentTrainingExample.Intent))
                    .Append(mlContext.Transforms.Text.FeaturizeText("Features", nameof(IntentTrainingExample.Utterance)))
                    .Append(mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy())
                    .Append(mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

                model = pipeline.Fit(trainingData);
                if (!string.IsNullOrWhiteSpace(_options.ModelPath))
                {
                    var directory = Path.GetDirectoryName(_options.ModelPath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    mlContext.Model.Save(model, trainingData.Schema, _options.ModelPath);
                    _logger.LogInformation("Saved baseline intent classification model to {Path}.", _options.ModelPath);
                }
            }

            var labels = ExtractLabels(mlContext, model);
            if (labels.Count == 0)
            {
                labels = IntentLabels.All;
            }

            return new ModelHolder(mlContext, model, labels);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize the ML.NET intent classifier.");
            return null;
        }
    }

    private static IReadOnlyList<IntentTrainingExample> CreateDefaultTrainingSet()
    {
        return new List<IntentTrainingExample>
        {
            new("hi", IntentLabels.Greeting),
            new("hello there", IntentLabels.Greeting),
            new("hey assistant", IntentLabels.Greeting),

            new("i want to place an order", IntentLabels.StartOrder),
            new("can i start an order", IntentLabels.StartOrder),
            new("let's start my order", IntentLabels.StartOrder),

            new("add a cheeseburger", IntentLabels.AddItem),
            new("i would like fries", IntentLabels.AddItem),
            new("get me a soda", IntentLabels.AddItem),

            new("change my fries to a salad", IntentLabels.ModifyOrder),
            new("modify the drink", IntentLabels.ModifyOrder),
            new("swap the burger for chicken", IntentLabels.ModifyOrder),

            new("cancel my order", IntentLabels.CancelOrder),
            new("i need to cancel", IntentLabels.CancelOrder),
            new("please cancel that order", IntentLabels.CancelOrder),

            new("what's the status of my order", IntentLabels.CheckStatus),
            new("is order ta 123 ready", IntentLabels.CheckStatus),
            new("check the order status", IntentLabels.CheckStatus),

            new("that's all", IntentLabels.CompleteOrder),
            new("i'm ready to checkout", IntentLabels.CompleteOrder),
            new("we are done", IntentLabels.CompleteOrder),

            new("yes please", IntentLabels.Affirm),
            new("go ahead", IntentLabels.Affirm),
            new("correct", IntentLabels.Affirm),

            new("no thanks", IntentLabels.Negate),
            new("not yet", IntentLabels.Negate),
            new("don't do that", IntentLabels.Negate),

            new("tell me a joke", IntentLabels.Fallback),
            new("play some music", IntentLabels.Fallback),
            new("what's the weather", IntentLabels.Fallback)
        };
    }

    private IReadOnlyList<string> ExtractLabels(MLContext mlContext, ITransformer model)
    {
        try
        {
            using var engine = mlContext.Model.CreatePredictionEngine<ModelInput, ModelOutput>(model);
            var slotNames = default(VBuffer<ReadOnlyMemory<char>>);
            engine.OutputSchema[nameof(ModelOutput.Score)].GetSlotNames(ref slotNames);
            return slotNames.DenseValues().Select(value => value.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to read label metadata from the intent model.");
            return Array.Empty<string>();
        }
    }

    private sealed class ModelHolder
    {
        private readonly IReadOnlyList<string> _labels;

        public ModelHolder(MLContext mlContext, ITransformer model, IReadOnlyList<string> labels)
        {
            MlContext = mlContext;
            Model = model;
            _labels = labels;
        }

        public MLContext MlContext { get; }

        public ITransformer Model { get; }

        public (string? Label, double Confidence) GetBestLabel(float[]? scores, string? predictedLabel)
        {
            if (scores is null || scores.Length == 0 || scores.Length != _labels.Count)
            {
                return string.IsNullOrWhiteSpace(predictedLabel)
                    ? (null, 0d)
                    : (predictedLabel, 0d);
            }

            var probabilities = ComputeSoftmax(scores);
            var maxIndex = 0;
            var maxValue = probabilities[0];

            for (var i = 1; i < probabilities.Length; i++)
            {
                if (probabilities[i] <= maxValue)
                {
                    continue;
                }

                maxIndex = i;
                maxValue = probabilities[i];
            }

            return (_labels[maxIndex], maxValue);
        }
    }

    private static double[] ComputeSoftmax(IReadOnlyList<float> scores)
    {
        var max = scores.Max();
        var exponentials = new double[scores.Count];
        double sum = 0d;

        for (var i = 0; i < scores.Count; i++)
        {
            var value = Math.Exp(scores[i] - max);
            exponentials[i] = value;
            sum += value;
        }

        if (sum <= 0d)
        {
            return Enumerable.Repeat(0d, scores.Count).ToArray();
        }

        for (var i = 0; i < exponentials.Length; i++)
        {
            exponentials[i] = exponentials[i] / sum;
        }

        return exponentials;
    }

    private sealed record IntentTrainingExample(string Utterance, string Intent);

    private sealed class ModelInput
    {
        public string Utterance { get; set; } = string.Empty;
    }

    private sealed class ModelOutput
    {
        [ColumnName("PredictedLabel")]
        public string? PredictedLabel { get; set; }

        public float[]? Score { get; set; }
    }
}
