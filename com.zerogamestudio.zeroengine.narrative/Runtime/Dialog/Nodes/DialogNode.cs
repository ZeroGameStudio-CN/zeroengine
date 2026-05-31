using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using ZeroEngine.Performance;

namespace ZeroEngine.Dialog
{
    /// <summary>
    /// Node types in the dialog system.
    /// </summary>
    public enum DialogNodeType
    {
        Start,          // Entry point
        Text,           // Display text/dialogue
        Choice,         // Present choices
        Condition,      // Conditional branch
        SetVariable,    // Set a variable
        Random,         // Random branch
        Callback,       // External callback
        End             // Exit point
    }

    /// <summary>
    /// Result of node execution.
    /// </summary>
    public struct DialogNodeResult
    {
        public bool WaitForInput;       // Should runner wait for user input?
        public string NextNodeId;       // Next node to execute (null = use default output)
        public DialogLine Line;         // Line to display (if applicable)
        public List<DialogChoice> Choices;  // Choices to present (if applicable)

        public static DialogNodeResult Continue(string nextNodeId = null)
        {
            return new DialogNodeResult { NextNodeId = nextNodeId };
        }

        public static DialogNodeResult WaitLine(DialogLine line)
        {
            return new DialogNodeResult { WaitForInput = true, Line = line };
        }

        public static DialogNodeResult WaitChoices(List<DialogChoice> choices)
        {
            return new DialogNodeResult { WaitForInput = true, Choices = choices };
        }

        public static DialogNodeResult End()
        {
            return new DialogNodeResult { NextNodeId = "__END__" };
        }
    }

    /// <summary>
    /// Base class for all dialog nodes.
    /// </summary>
    [Serializable]
    public abstract class DialogNode
    {
        [Tooltip("Unique identifier for this node")]
        public string Id;

        [Tooltip("Node type (auto-set)")]
        public DialogNodeType Type;

        [Tooltip("Position in graph editor")]
        public Vector2 EditorPosition;

        [Tooltip("Optional comment/note")]
        public string Comment;

        /// <summary>
        /// Default output connection.
        /// </summary>
        public string OutputNodeId;

        /// <summary>
        /// Execute this node.
        /// </summary>
        public abstract DialogNodeResult Execute(DialogGraphContext context);

        /// <summary>
        /// Get all output node IDs from this node.
        /// </summary>
        public virtual List<string> GetOutputNodeIds()
        {
            var outputs = new List<string>(1);
            if (!string.IsNullOrEmpty(OutputNodeId))
            {
                outputs.Add(OutputNodeId);
            }
            return outputs;
        }
    }

    /// <summary>
    /// Start node - entry point of dialog.
    /// </summary>
    [Serializable]
    public class DialogStartNode : DialogNode
    {
        public DialogStartNode()
        {
            Type = DialogNodeType.Start;
            Id = "Start";
        }

        public override DialogNodeResult Execute(DialogGraphContext context)
        {
            // Start node just passes through
            return DialogNodeResult.Continue(OutputNodeId);
        }
    }

    /// <summary>
    /// Text node - displays dialogue text.
    /// </summary>
    [Serializable]
    public class DialogTextNode : DialogNode
    {
        [Tooltip("Speaker name")]
        public string Speaker;

        [TextArea(3, 6)]
        [Tooltip("Dialogue text (supports {variable} substitution)")]
        public string Text;

        [Tooltip("Localization key")]
        public string LocalizationKey;

        [Tooltip("Voice clip key")]
        public string VoiceKey;

        [Tooltip("Portrait/expression key")]
        public string PortraitKey;

        [Tooltip("Custom metadata")]
        public List<StringPair> Metadata = new();

        public DialogTextNode()
        {
            Type = DialogNodeType.Text;
        }

        public override DialogNodeResult Execute(DialogGraphContext context)
        {
            // Substitute variables in text
            string processedText = SubstituteVariables(Text, context.Variables);

            var line = new DialogLine
            {
                Speaker = Speaker,
                Text = processedText,
                LocalizationKey = LocalizationKey,
                VoiceKey = VoiceKey,
                PortraitKey = PortraitKey
            };

            // Copy metadata
            if (Metadata != null && Metadata.Count > 0)
            {
                line.Metadata = new Dictionary<string, string>(Metadata.Count);
                foreach (var pair in Metadata)
                {
                    line.Metadata[pair.Key] = pair.Value;
                }
            }

            context.LastLine = line;
            return DialogNodeResult.WaitLine(line);
        }

        // Shared StringBuilder to reduce allocations
        [ThreadStatic]
        private static StringBuilder _substituteBuilder;

        private static string SubstituteVariables(string text, DialogVariables variables)
        {
            if (string.IsNullOrEmpty(text) || !text.Contains("{"))
                return text;

            _substituteBuilder ??= new StringBuilder(256);
            _substituteBuilder.Clear();

            int lastEnd = 0;
            int start = 0;

            while (true)
            {
                int openBrace = text.IndexOf('{', start);
                if (openBrace < 0) break;

                int closeBrace = text.IndexOf('}', openBrace);
                if (closeBrace < 0) break;

                // Append text before the variable
                if (openBrace > lastEnd)
                {
                    _substituteBuilder.Append(text, lastEnd, openBrace - lastEnd);
                }

                // Extract variable name and append its value
                string varName = text.Substring(openBrace + 1, closeBrace - openBrace - 1);
                object value = variables.Get(varName);
                if (value != null)
                {
                    _substituteBuilder.Append(value);
                }

                lastEnd = closeBrace + 1;
                start = lastEnd;
            }

            // Append remaining text
            if (lastEnd < text.Length)
            {
                _substituteBuilder.Append(text, lastEnd, text.Length - lastEnd);
            }

            return _substituteBuilder.ToString();
        }

        /// <summary>
        /// Public accessor for variable substitution (used by other nodes).
        /// </summary>
        public static string SubstituteVariablesPublic(string text, DialogVariables variables)
        {
            return SubstituteVariables(text, variables);
        }
    }

    /// <summary>
    /// Choice node - presents multiple choices.
    /// </summary>
    [Serializable]
    public class DialogChoiceNode : DialogNode
    {
        [Serializable]
        public class ChoiceOption
        {
            [Tooltip("Choice display text")]
            public string Text;

            [Tooltip("Localization key")]
            public string LocalizationKey;

            [Tooltip("Target node ID when selected")]
            public string TargetNodeId;

            [Tooltip("Condition expression (empty = always available)")]
            public string Condition;

            [Tooltip("Show but disable if condition fails (vs hide)")]
            public bool ShowIfDisabled;
        }

        [Tooltip("Optional prompt text before choices")]
        public string PromptText;

        [Tooltip("Available choices")]
        public List<ChoiceOption> Choices = new();

        public DialogChoiceNode()
        {
            Type = DialogNodeType.Choice;
        }

        public override DialogNodeResult Execute(DialogGraphContext context)
        {
            // Use pooled lists to avoid GC allocations
            var availableChoices = new List<DialogChoice>(Choices.Count);
            var validTargets = new List<string>(Choices.Count);

            for (int i = 0; i < Choices.Count; i++)
            {
                var opt = Choices[i];
                bool conditionMet = string.IsNullOrEmpty(opt.Condition) ||
                                    DialogConditionParser.Evaluate(opt.Condition, context.Variables);

                if (!conditionMet && !opt.ShowIfDisabled)
                    continue;

                var choice = new DialogChoice
                {
                    Text = DialogTextNode.SubstituteVariablesPublic(opt.Text, context.Variables),
                    LocalizationKey = opt.LocalizationKey,
                    TargetIndex = validTargets.Count,
                    Condition = opt.Condition,
                    IsEnabled = conditionMet
                };

                availableChoices.Add(choice);
                validTargets.Add(opt.TargetNodeId);
            }

            context.PendingChoiceTargets = validTargets;
            return DialogNodeResult.WaitChoices(availableChoices);
        }

        public override List<string> GetOutputNodeIds()
        {
            var outputs = new List<string>(Choices.Count);
            foreach (var choice in Choices)
            {
                if (!string.IsNullOrEmpty(choice.TargetNodeId) && !outputs.Contains(choice.TargetNodeId))
                {
                    outputs.Add(choice.TargetNodeId);
                }
            }
            return outputs;
        }
    }

    /// <summary>
    /// Condition node - conditional branching.
    /// </summary>
    [Serializable]
    public class DialogConditionNode : DialogNode
    {
        [Tooltip("Condition expression (e.g., 'gold >= 100 && hasKey')")]
        public string Condition;

        [Tooltip("Node ID if condition is true")]
        public string TrueNodeId;

        [Tooltip("Node ID if condition is false")]
        public string FalseNodeId;

        public DialogConditionNode()
        {
            Type = DialogNodeType.Condition;
        }

        public override DialogNodeResult Execute(DialogGraphContext context)
        {
            bool result = DialogConditionParser.Evaluate(Condition, context.Variables);
            string nextNode = result ? TrueNodeId : FalseNodeId;
            return DialogNodeResult.Continue(nextNode);
        }

        public override List<string> GetOutputNodeIds()
        {
            var outputs = new List<string>(2);
            if (!string.IsNullOrEmpty(TrueNodeId)) outputs.Add(TrueNodeId);
            if (!string.IsNullOrEmpty(FalseNodeId)) outputs.Add(FalseNodeId);
            return outputs;
        }
    }

    /// <summary>
    /// SetVariable node - sets a variable value.
    /// </summary>
    [Serializable]
    public class DialogSetVariableNode : DialogNode
    {
        public enum Operation
        {
            Set,        // variable = value
            Add,        // variable += value (numeric)
            Subtract,   // variable -= value (numeric)
            Multiply,   // variable *= value (numeric)
            Divide,     // variable /= value (numeric)
            Toggle      // variable = !variable (bool)
        }

        [Tooltip("Variable name")]
        public string Variable;

        [Tooltip("Operation to perform")]
        public Operation Op = Operation.Set;

        [Tooltip("Value (parsed based on current type or inferred)")]
        public string Value;

        [Tooltip("Variable type hint")]
        public DialogVariableType TypeHint = DialogVariableType.String;

        public DialogSetVariableNode()
        {
            Type = DialogNodeType.SetVariable;
        }

        public override DialogNodeResult Execute(DialogGraphContext context)
        {
            if (string.IsNullOrEmpty(Variable))
                return DialogNodeResult.Continue(OutputNodeId);

            object currentValue = context.Variables.Get(Variable);
            object newValue = CalculateNewValue(currentValue, Op, Value, TypeHint);

            context.Variables.Set(Variable, newValue);
            return DialogNodeResult.Continue(OutputNodeId);
        }

        private static object CalculateNewValue(object current, Operation op, string valueStr, DialogVariableType hint)
        {
            if (op == Operation.Toggle)
            {
                bool currentBool = current is bool b ? b : DialogVariables.IsTruthyValue(current);
                return !currentBool;
            }

            object parsedValue = ParseValue(valueStr, hint);

            if (op == Operation.Set)
            {
                return parsedValue;
            }

            // Numeric operations
            double currentNum = current != null ? Convert.ToDouble(current) : 0;
            double valueNum = Convert.ToDouble(parsedValue);

            double result = op switch
            {
                Operation.Add => currentNum + valueNum,
                Operation.Subtract => currentNum - valueNum,
                Operation.Multiply => currentNum * valueNum,
                Operation.Divide => valueNum != 0 ? currentNum / valueNum : 0,
                _ => valueNum
            };

            // Return appropriate type
            return hint switch
            {
                DialogVariableType.Int => (int)result,
                DialogVariableType.Float => (float)result,
                _ => result
            };
        }

        private static object ParseValue(string valueStr, DialogVariableType hint)
        {
            if (string.IsNullOrEmpty(valueStr)) return GetDefaultValue(hint);

            return hint switch
            {
                DialogVariableType.Bool => bool.TryParse(valueStr, out bool b) ? b :
                                           (valueStr == "1" || string.Equals(valueStr, "true", StringComparison.OrdinalIgnoreCase)),
                DialogVariableType.Int => int.TryParse(valueStr, out int i) ? i : 0,
                DialogVariableType.Float => float.TryParse(valueStr, out float f) ? f : 0f,
                DialogVariableType.String => valueStr,
                _ => valueStr
            };
        }

        private static object GetDefaultValue(DialogVariableType type)
        {
            return type switch
            {
                DialogVariableType.Bool => false,
                DialogVariableType.Int => 0,
                DialogVariableType.Float => 0f,
                DialogVariableType.String => string.Empty,
                _ => null
            };
        }
    }

    /// <summary>
    /// Random node - random branching.
    /// </summary>
    [Serializable]
    public class DialogRandomNode : DialogNode
    {
        [Serializable]
        public class WeightedOutput
        {
            [Tooltip("Target node ID")]
            public string NodeId;

            [Tooltip("Selection weight (higher = more likely)")]
            [Range(1, 100)]
            public int Weight = 1;
        }

        [Tooltip("Weighted random outputs")]
        public List<WeightedOutput> Outputs = new();

        public DialogRandomNode()
        {
            Type = DialogNodeType.Random;
        }

        public override DialogNodeResult Execute(DialogGraphContext context)
        {
            if (Outputs == null || Outputs.Count == 0)
                return DialogNodeResult.Continue(OutputNodeId);

            int totalWeight = 0;
            foreach (var output in Outputs)
            {
                totalWeight += output.Weight;
            }

            int roll = UnityEngine.Random.Range(0, totalWeight);
            int accumulated = 0;

            foreach (var output in Outputs)
            {
                accumulated += output.Weight;
                if (roll < accumulated)
                {
                    return DialogNodeResult.Continue(output.NodeId);
                }
            }

            return DialogNodeResult.Continue(Outputs[Outputs.Count - 1].NodeId);
        }

        public override List<string> GetOutputNodeIds()
        {
            var outputs = new List<string>(Outputs.Count);
            foreach (var output in Outputs)
            {
                if (!string.IsNullOrEmpty(output.NodeId) && !outputs.Contains(output.NodeId))
                {
                    outputs.Add(output.NodeId);
                }
            }
            return outputs;
        }
    }

    /// <summary>
    /// Callback node - triggers external callback.
    /// </summary>
    [Serializable]
    public class DialogCallbackNode : DialogNode
    {
        [Tooltip("Callback identifier")]
        public string CallbackId;

        [Tooltip("Optional parameter")]
        public string Parameter;

        [Tooltip("Wait for callback to complete before continuing")]
        public bool WaitForCompletion;

        public DialogCallbackNode()
        {
            Type = DialogNodeType.Callback;
        }

        public override DialogNodeResult Execute(DialogGraphContext context)
        {
            context.TriggerCallback(CallbackId, Parameter);

            if (WaitForCompletion)
            {
                context.WaitingForCallback = CallbackId;
                return new DialogNodeResult { WaitForInput = true };
            }

            return DialogNodeResult.Continue(OutputNodeId);
        }
    }

    /// <summary>
    /// End node - marks end of dialog.
    /// </summary>
    [Serializable]
    public class DialogEndNode : DialogNode
    {
        [Tooltip("End tag for identifying specific endings")]
        public string EndTag;

        public DialogEndNode()
        {
            Type = DialogNodeType.End;
            Id = "End";
        }

        public override DialogNodeResult Execute(DialogGraphContext context)
        {
            context.EndTag = EndTag;
            return DialogNodeResult.End();
        }

        public override List<string> GetOutputNodeIds()
        {
            return new List<string>(); // No outputs
        }
    }

    /// <summary>
    /// Helper struct for key-value pairs.
    /// </summary>
    [Serializable]
    public struct StringPair
    {
        public string Key;
        public string Value;
    }
}
