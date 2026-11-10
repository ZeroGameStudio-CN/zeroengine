using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ZeroEngine.PortraitSpeech.Editor;
using Object=UnityEngine.Object;

namespace ZeroEngine.PortraitSpeech.Tests
{
    public sealed class PortraitMaintenanceTests
    {
        [Serializable] private sealed class ActorFixture { public int id; public Sprite spritePortrait; public Sprite[] spritePortraits; }
        private sealed class DatabaseFixture : ScriptableObject { public ActorFixture[] actors; }
        private sealed class PreviewLibraryFixture : PortraitSpeechLibrary
        {
            public Sprite Replacement;
            public override Sprite ResolveAuthoringPortrait(int actorId,Sprite source)=>actorId==3?Replacement:source;
        }
        [Test] public void HostPreviewArtIsResolvedBeforeAuthoringDeduplication()
        {
            var host=ScriptableObject.CreateInstance<PreviewLibraryFixture>();
            var database=ScriptableObject.CreateInstance<DatabaseFixture>();
            var replacement=Sprite.Create(sprite.texture,sprite.rect,sprite.pivot/sprite.rect.size);
            replacement.name="PreviewFace";
            try
            {
                host.Replacement=replacement;
                database.actors=new[]{new ActorFixture{id=3,spritePortrait=sprite,spritePortraits=new[]{sprite}},new ActorFixture{id=4,spritePortrait=sprite}};
                var found=DialoguePortraitAuthoringUtility.Collect(host,database);
                Assert.That(found.Count,Is.EqualTo(2));
                Assert.That(found.Single(a=>a.ActorId==3).Sprite,Is.SameAs(replacement));
                Assert.That(found.Single(a=>a.ActorId==4).Sprite,Is.SameAs(sprite));
                Assert.That(library.ResolveAuthoringPortrait(3,sprite),Is.SameAs(sprite));
            }
            finally{Object.DestroyImmediate(host);Object.DestroyImmediate(database);Object.DestroyImmediate(replacement);}
        }
        private string root;
        private Sprite sprite;
        private PortraitSpeechLibrary library;
        private DialoguePortraitAuthoringUtility.Avatar[] avatars;
        [SetUp] public void SetUp()
        {
            root="Assets/__PortraitMaintenance_"+Guid.NewGuid().ToString("N");AssetDatabase.CreateFolder("Assets",Path.GetFileName(root));
            var texture=new Texture2D(24,24,TextureFormat.RGBA32,false);
            try
            {
                var pixels=Enumerable.Repeat(new Color32(255,225,215,255),24*24).ToArray();
                for(int y=8;y<=12;y++)for(int x=8;x<=15;x++)pixels[y*24+x]=new Color32(194,60,64,255);
                texture.SetPixels32(pixels);texture.Apply();File.WriteAllBytes(root+"/Face.png",texture.EncodeToPNG());
            }
            finally{Object.DestroyImmediate(texture);}
            AssetDatabase.ImportAsset(root+"/Face.png",ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(root+"/Face.png");
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.npotScale=TextureImporterNPOTScale.None;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
            sprite=AssetDatabase.LoadAssetAtPath<Sprite>(root+"/Face.png");
            library=ScriptableObject.CreateInstance<PortraitSpeechLibrary>();
            library.portraitSpeech=new[]{new PortraitSpeechProfile{actorId=2,spriteName=sprite.name,mouth=new Rect(7f/24,7f/24,10f/24,7f/24),skinSample=new Vector2(19.5f/24,10.5f/24),sampleSkinByRow=true}};
            avatars=new[]{new DialoguePortraitAuthoringUtility.Avatar{ActorId=2,ActorName="Fixture",Sprite=sprite}};
        }
        [TearDown] public void TearDown()
        {
            if(library){Undo.ClearUndo(library);Object.DestroyImmediate(library);}
            if(AssetDatabase.IsValidFolder(root))AssetDatabase.DeleteAsset(root);
        }
        private Texture2D MakeSheet()
        {
            var canvas=DialoguePortraitAuthoringUtility.ReadCanvas(sprite);
            try
            {
                using var pixels=new PortraitMouthPoseDraft(canvas,library.portraitSpeech[0].mouth);
                pixels.Generate(library.portraitSpeech[0].skinSample,true);return pixels.SaveNewAsset(root+"/ArtistSheet.png");
            }
            finally{Object.DestroyImmediate(canvas);}
        }
        [Test] public void ManualSheetsRemainProtectedAndStampsSurviveSerialization()
        {
            library.portraitSpeech[0].mouthPoseSheet=MakeSheet();var original=library.portraitSpeech[0].mouthPoseSheet;
            using(var plan=PortraitLibraryMaintenance.Scan(library,avatars))
            {
                Assert.That(plan.Entries.Single().Protected,Is.True);Assert.That(plan.GenerateCandidates(),Is.Zero);
                var row=plan.Entries.Single();plan.Confirm(row,row.Proposed);Assert.That(plan.ApplyConfirmed(root),Is.EqualTo(1));
            }
            var copy=ScriptableObject.CreateInstance<PortraitSpeechLibrary>();
            try
            {
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(library),copy);
                using var next=PortraitLibraryMaintenance.Scan(copy,avatars);
                Assert.That(next.Entries.Single().Status,Is.EqualTo(PortraitLibraryMaintenance.Change.Current));
                Assert.That(next.Entries.Single().Protected,Is.True);Assert.That(copy.portraitSpeech[0].mouthPoseSheet,Is.EqualTo(original));
            }
            finally{Object.DestroyImmediate(copy);}
        }
        [Test] public void BatchNeverAppliesUnconfirmedOrPostConfirmationEdits()
        {
            using var plan=PortraitLibraryMaintenance.Scan(library,avatars);
            Assert.That(plan.GenerateCandidates(),Is.EqualTo(1));Assert.That(plan.ApplyConfirmed(root),Is.Zero);
            var row=plan.Entries.Single();plan.Confirm(row,row.Proposed);
            row.Draft.Paint(0,3,3,new Color32(55,5,8,255));row.Draft.EndStroke();
            Assert.Throws<InvalidOperationException>(()=>plan.ApplyConfirmed(root));
            Assert.That(row.Confirmed,Is.False);Assert.That(library.portraitSpeech[0].mouthPoseSheet,Is.Null);
            Assert.That(plan.CreatedAssets,Is.Empty);
        }
        [Test] public void RawSourceChangesAndModifiedSheetsAreDetectedWithoutOverwriting()
        {
            string generatedPath;
            using(var plan=PortraitLibraryMaintenance.Scan(library,avatars))
            {
                plan.GenerateCandidates();var row=plan.Entries.Single();plan.Confirm(row,row.Proposed);plan.ApplyConfirmed(root);
                generatedPath=AssetDatabase.GetAssetPath(library.portraitSpeech[0].mouthPoseSheet);
            }
            byte[] original=File.ReadAllBytes(generatedPath);var decoded=new Texture2D(2,2);
            try
            {
                ImageConversion.LoadImage(decoded,original);decoded.SetPixel(0,0,Color.red);decoded.Apply();File.WriteAllBytes(generatedPath,decoded.EncodeToPNG());
                using(var edited=PortraitLibraryMaintenance.Scan(library,avatars))
                {Assert.That(edited.Entries.Single().Status,Is.EqualTo(PortraitLibraryMaintenance.Change.SheetEdited));Assert.That(edited.Entries.Single().Protected,Is.True);Assert.That(edited.GenerateCandidates(),Is.Zero);}
                var sourcePath=AssetDatabase.GetAssetPath(sprite);ImageConversion.LoadImage(decoded,File.ReadAllBytes(sourcePath));
                decoded.SetPixel(1,1,Color.red);decoded.Apply();File.WriteAllBytes(sourcePath,decoded.EncodeToPNG());
                using var changed=PortraitLibraryMaintenance.Scan(library,avatars);
                Assert.That(changed.Entries.Single().Status,Is.EqualTo(PortraitLibraryMaintenance.Change.SourceChanged));
                Assert.That(changed.Entries.Single().Protected,Is.True);
            }
            finally{Object.DestroyImmediate(decoded);}
        }
        [Test] public void GeneratorRecipeChangeRebuildsGeneratedSheetsButProtectsManualOnes()
        {
            var original=library.portraitSpeech[0];var manual=original;manual.actorId=3;manual.mouthPoseSheet=MakeSheet();
            library.portraitSpeech=new[]{original,manual};
            var all=avatars.Concat(new[]{new DialoguePortraitAuthoringUtility.Avatar{ActorId=3,ActorName="Manual",Sprite=sprite}}).ToArray();
            using(var plan=PortraitLibraryMaintenance.Scan(library,all))
            {
                Assert.That(plan.GenerateCandidates(),Is.EqualTo(1));
                foreach(var row in plan.Entries)plan.Confirm(row,row.Proposed);
                Assert.That(plan.ApplyConfirmed(root),Is.EqualTo(2));
            }
            using(var serialized=new SerializedObject(library))
            {
                var state=serialized.FindProperty("portraitAuthoringStateJson");
                state.stringValue=state.stringValue.Replace(PortraitLibraryMaintenance.Recipe,"old-generator");
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            using var update=PortraitLibraryMaintenance.Scan(library,all);
            Assert.That(update.Entries.Single(e=>e.Before.actorId==2).Status,Is.EqualTo(PortraitLibraryMaintenance.Change.ToolChanged));
            Assert.That(update.Entries.Single(e=>e.Before.actorId==3).Protected,Is.True);
            Assert.That(update.GenerateCandidates(),Is.EqualTo(1));
        }
        [Test] public void SharedTimingAndPlaybackPauseDoNotInvalidateCalibration()
        {
            using(var plan=PortraitLibraryMaintenance.Scan(library,avatars))
            {plan.GenerateCandidates();var row=plan.Entries.Single();plan.Confirm(row,row.Proposed);plan.ApplyConfirmed(root);}
            library.portraitSpeechFramesPerSecond=6;library.portraitSpeech[0].speechDisabled=true;
            using var next=PortraitLibraryMaintenance.Scan(library,avatars);
            Assert.That(next.Entries.Single().Status,Is.EqualTo(PortraitLibraryMaintenance.Change.Current));
            Assert.That(next.GenerateCandidates(),Is.Zero);
        }
    }
}
