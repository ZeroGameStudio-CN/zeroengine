using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.Render.Tests.Editor
{
    public sealed class BloodStainMaskTests
    {
        private static BloodStainMask Canvas(uint seed=41)
        {
            return new BloodStainMask(seed);
        }
        private static int Count(BloodStainMask mask)
        {int found=0;foreach(var pixel in mask.Pixels)if(pixel.a>0)found++;return found;}
        private static long Opacity(BloodStainMask mask)
        {long total=0;foreach(var pixel in mask.Pixels)total+=pixel.a;return total;}
        private static ulong Hash(BloodStainMask mask)
        {ulong value=1469598103934665603;foreach(var pixel in mask.Pixels){value^=(uint)(pixel.r|(pixel.a<<8));value*=1099511628211;}return value;}
        [Test] public void AccumulationIsBoundedAndPauseDoesNotEraseStains()
        {
            var mask=Canvas();for(int i=0;i<30;i++)mask.Deposit(.12f);
            Assert.That(mask.Intensity,Is.EqualTo(1));Assert.That(mask.Tier,Is.EqualTo(3));
            mask.Advance(3);Assert.That(mask.Intensity,Is.EqualTo(1));
            mask.Advance(0);Assert.That(mask.Intensity,Is.EqualTo(1));
            mask.Advance(2.5f);Assert.That(mask.Intensity,Is.EqualTo(.5f).Within(.0001));
            mask.Advance(2.5f);Assert.That(mask.Intensity,Is.Zero);Assert.That(mask.Tier,Is.Zero);
            mask.Deposit(.12f);Assert.That(mask.Intensity,Is.EqualTo(.12f).Within(.0001));
        }
        [Test] public void FreshBloodKeepsOnlyTheRemainingIntensity_AndRestartsRetention()
        {
            var mask=Canvas();mask.Deposit(.4f);mask.Advance(5);
            float faded=mask.Intensity;Assert.That(faded,Is.LessThan(.4f));
            mask.Deposit(.12f);float fresh=mask.Intensity;mask.Advance(3);
            Assert.That(fresh,Is.EqualTo(faded+.12f).Within(.0001));Assert.That(mask.Intensity,Is.EqualTo(fresh));
            mask.Deposit(-1);mask.Deposit(float.NaN);mask.Deposit(float.PositiveInfinity);
            Assert.That(mask.Intensity,Is.EqualTo(fresh));
        }
        [Test] public void ExistingColourRegionsStayFixedDuringMovementTimeAndFade()
        {
            var mask=Canvas();mask.Deposit(.2f);Assert.That(mask.Flush(),Is.True);
            ulong pixels=Hash(mask);int revision=mask.Revision;
            for(int i=0;i<300;i++){mask.Advance(.02f);Assert.That(mask.Flush(),Is.False);}
            Assert.That(mask.Intensity,Is.LessThan(.2f));Assert.That(Hash(mask),Is.EqualTo(pixels));
            Assert.That(mask.Revision,Is.EqualTo(revision),"Moving and fading must not redraw or shuffle the CPU canvas.");
        }
        [Test] public void FreshSplashesVaryButNeverMoveOrEraseExistingMarksDuringTheHold()
        {
            var mask=Canvas();mask.Deposit(.2f);mask.Flush();var first=(Color32[])mask.Pixels.Clone();ulong before=Hash(mask);
            mask.Deposit(.2f);mask.Flush();Assert.That(Hash(mask),Is.Not.EqualTo(before));
            for(int i=0;i<first.Length;i++)Assert.That(mask.Pixels[i].a,Is.GreaterThanOrEqualTo(first[i].a));
            var repeat=Canvas();repeat.Deposit(.2f);repeat.Flush();repeat.Deposit(.2f);repeat.Flush();
            Assert.That(Hash(repeat),Is.EqualTo(Hash(mask)),"Seeded splash placement must be reproducible.");
        }
        [Test] public void MassDeathsUseOneFixedLookupAndBoundTheColourWorkAndOpacity()
        {
            var mask=Canvas();var pixels=mask.Pixels;
            for(int i=0;i<10000;i++)mask.Deposit(.22f);
            Assert.That(mask.Flush(),Is.True);Assert.That(mask.LastUpdatedColours,Is.EqualTo(4096));
            Assert.That(mask.Pixels,Is.SameAs(pixels));Assert.That(mask.Pixels.Length,Is.EqualTo(64*64));
            Assert.That(mask.Revision,Is.EqualTo(1));Assert.That(Count(mask),Is.GreaterThan(0));
            foreach(var pixel in pixels)Assert.That(pixel.a,Is.LessThanOrEqualTo(BloodStainMask.MaximumAlpha));
            Assert.That(mask.Flush(),Is.False);
        }
        [Test] public void SmallSplashesSpreadAcrossComplementaryGroups_WithoutShufflingExistingPattern()
        {
            foreach(uint seed in new uint[]{1,41,1709})
            {
                var mask=Canvas(seed);mask.Deposit(.06f);mask.Flush();
                Assert.That(Count(mask),Is.InRange(1600,2500),"A small splash should scatter over a bounded subset, rather than wash every colour.");
                var first=(Color32[])mask.Pixels.Clone();mask.Deposit(.06f);mask.Flush();
                Assert.That(Count(mask),Is.EqualTo(4096),"The next splash must fill the complementary groups before repeatedly deepening one region.");
                for(int i=0;i<first.Length;i++)
                {
                    Assert.That(mask.Pixels[i].r,Is.EqualTo(first[i].r),"New blood must not re-randomize existing source-feature patterns.");
                    if(first[i].a>0)Assert.That(mask.Pixels[i].a,Is.EqualTo(first[i].a));
                }
            }
        }
        [Test] public void FullyExpiredBloodIsClearedBeforeTheNextSmallSplash()
        {
            var mask=Canvas();for(int i=0;i<12;i++){mask.Deposit(.22f);mask.Flush();}
            long heavy=Opacity(mask);mask.Advance(8);Assert.That(mask.Intensity,Is.Zero);
            mask.Deposit(.12f);mask.Flush();Assert.That(mask.Intensity,Is.EqualTo(.12f).Within(.0001));
            Assert.That(Opacity(mask),Is.LessThan(heavy/2),"A fresh small splash must not resurrect the previous heavy coverage.");
        }
        [Test] public void PooledResetClearsVisibleAndQueuedBloodBeforeTheNextLife()
        {
            var mask=Canvas();mask.Deposit(.8f);mask.Flush();mask.Deposit(.4f);
            mask.ResetForPool();
            Assert.That(mask.Intensity,Is.Zero);Assert.That(Count(mask),Is.Zero);
            Assert.That(mask.HasPending,Is.False);Assert.That(mask.Flush(),Is.False);
            mask.Deposit(.06f);mask.Flush();
            Assert.That(mask.Intensity,Is.EqualTo(.06f).Within(.0001));
            Assert.That(Count(mask),Is.InRange(1600,2500));
        }
    }
}
