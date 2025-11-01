# Voice intent classifier integration

## Model choice

* Library: [ML.NET](https://learn.microsoft.com/dotnet/machine-learning/), loaded via `Microsoft.ML`.
* Algorithm: `SdcaMaximumEntropy` multi-class classifier over text embeddings produced by `FeaturizeText`.
* The service trains a lightweight baseline in memory when no pre-trained `.zip` model is supplied. The resulting model is stored at `data/models/voice-intents.zip` when the optional `ModelPath` setting is provided.

## Training data

The embedded baseline dataset is defined in `MlNetIntentClassifier.CreateDefaultTrainingSet()` and includes labelled utterances for the following intents:

| Intent label | Example utterances |
| --- | --- |
| `smalltalk.greeting` | "hi", "hello there", "hey assistant" |
| `order.start` | "i want to place an order", "can i start an order", "let's start my order" |
| `order.add_item` | "add a cheeseburger", "i would like fries", "get me a soda" |
| `order.modify` | "change my fries to a salad", "modify the drink", "swap the burger for chicken" |
| `order.cancel` | "cancel my order", "i need to cancel", "please cancel that order" |
| `order.check_status` | "what's the status of my order", "is order ta 123 ready", "check the order status" |
| `order.complete` | "that's all", "i'm ready to checkout", "we are done" |
| `dialog.affirm` | "yes please", "go ahead", "correct" |
| `dialog.negate` | "no thanks", "not yet", "don't do that" |
| `fallback.unknown` | "tell me a joke", "play some music", "what's the weather" |

Teams can replace this dataset by training their own ML.NET model and pointing the `IntentClassifier:ModelPath` setting at the exported `.zip` file.

## Integration details

* Service implementation: `VoiceDialog/IntentClassification/MlNetIntentClassifier.cs`
  * Loads a pre-trained model when `IntentClassifier:ModelPath` exists; otherwise trains the baseline dataset at startup.
  * Applies a configurable `MinimumConfidence` threshold (`appsettings*.json`) before surfacing predictions.
* Dependency injection: registered in `Program.cs` as `IIntentClassifier` when the feature is enabled.
* Voice endpoint: `Endpoints/VoiceEndpoints.cs` calls `PredictIntent` after transcription, forwarding the top label/score as event metadata (`intent.label`, `intent.score`).
* Dialog FSM updates (`VoiceDialogStateMachine.cs`):
  * Stores the last detected intent in the session metadata (`intent.last`).
  * Uses classifier output to influence transitions in `Start`, `Ordering`, `Modifying`, `Cancelling`, `CheckingStatus`, and `Confirming` states, while falling back to keyword heuristics when no confident prediction is available.
* Configuration: set `IntentClassifier:Enabled` to `false` to bypass the classifier without removing the service registration.

Refer to `Options/IntentClassifierOptions.cs` for all available settings.
