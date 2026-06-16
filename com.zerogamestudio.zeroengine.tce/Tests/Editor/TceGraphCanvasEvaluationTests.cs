using System.IO;
using NUnit.Framework;

namespace ZeroEngine.TCE.Tests.Editor
{
    [TestFixture]
    public sealed class TceGraphCanvasEvaluationTests
    {
        private const string PackagePath = "Packages/com.zerogamestudio.zeroengine.tce";
        private const string EvaluationPath = PackagePath + "/Documentation~/graph-canvas-evaluation.md";

        [Test]
        public void GraphCanvasEvaluation_DocumentExistsAndRecordsDecision()
        {
            Assert.IsTrue(File.Exists(EvaluationPath), "Graph canvas evaluation document must exist.");

            string document = File.ReadAllText(EvaluationPath);

            StringAssert.Contains("Decision", document);
            StringAssert.Contains("keep the productized lane editor", document);
            StringAssert.Contains("custom UI Toolkit canvas", document);
        }

        [Test]
        public void GraphCanvasEvaluation_PreservesTceProductizationApis()
        {
            string document = File.ReadAllText(EvaluationPath);

            StringAssert.Contains("TceGraph", document);
            StringAssert.Contains("TceComponentCatalogBuilder", document);
            StringAssert.Contains("TceGraphValidator", document);
            StringAssert.Contains("TcePreviewRunner", document);
            StringAssert.Contains("TceGraphSchema", document);
            StringAssert.Contains("TceGraphMigrationRegistry", document);
            StringAssert.Contains("TceExternalGraphImporter", document);
        }

        [Test]
        public void GraphCanvasEvaluation_ComparedCanvasOptions()
        {
            string document = File.ReadAllText(EvaluationPath);

            StringAssert.Contains("GraphView", document);
            StringAssert.Contains("custom UI Toolkit canvas", document);
            StringAssert.Contains("lane editor", document);
        }

        [Test]
        public void EditorSources_DoNotIntroduceGraphViewImplementationYet()
        {
            foreach (string file in Directory.GetFiles($"{PackagePath}/Editor", "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(file);

                StringAssert.DoesNotContain("UnityEditor.Experimental.GraphView", source, file);
            }
        }
    }
}
