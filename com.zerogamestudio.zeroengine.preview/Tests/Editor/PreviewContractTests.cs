using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace ZeroEngine.Preview.Tests
{
    public sealed class PreviewContractTests
    {
        private sealed class Adapter : IPreviewAdapter
        {
            public int Mutations,Resets;
            public bool Throw,ThrowReset;
            public TaskCompletionSource<string> Pending;
            public IReadOnlyList<PreviewChoice> Choices {get;set;}=new[]{new PreviewChoice("a","A")};
            public string ReadStatus()=>"unchanged";
            public bool CanExecute(PreviewRequest request,PreviewContext context,out string reason){reason=null;return true;}
            public Task<string> ExecuteAsync(PreviewRequest request,PreviewContext context)
            {Mutations++;if(Throw)throw new InvalidOperationException("start failed");return Pending?.Task??Task.FromResult("ok");}
            public void Reset(PreviewResetReason reason){Resets++;if(ThrowReset)throw new InvalidOperationException("cleanup failed");}
        }
        private static PreviewRegistration Entry(string id,Adapter adapter,PreviewMode mode=PreviewMode.Toggle)=>new(new PreviewDefinition(id,id,"tests","boundary","entry",mode),adapter);
        [Test] public void IdentityAndQueriesAreReadOnly()
        {
            var adapter=new Adapter();using var c=new PreviewController(new[]{Entry("ui",adapter)},()=>new PreviewContext(true,false));
            foreach(var command in new[]{("status","ui"),("help","all"),("on","all"),("on","missing"),("invalid","ui")})c.ExecuteAsync(command.Item1,command.Item2).GetAwaiter().GetResult();
            Assert.That(adapter.Mutations,Is.Zero);Assert.Throws<ArgumentException>(()=>new PreviewController(new[]{Entry("ui",adapter),Entry("ui",adapter)},()=>default));
            Assert.Throws<ArgumentException>(()=>new PreviewDefinition("bad id","x","x","","",PreviewMode.Toggle));
        }
        [Test] public void ReservedAllIdCannotShadowReadOnlyGroupQueries()
        {
            Assert.Throws<ArgumentException>(()=>Entry("all",new Adapter()));
        }
        [Test] public void UndefinedCapabilitiesAreRejectedAtDeclaration()
        {
            Assert.Throws<ArgumentOutOfRangeException>(()=>Entry("candidate",new Adapter(),(PreviewMode)99));
        }
        [Test] public void ProductionRejectsAllMutationsAndNeverResolvesASelection()
        {
            var adapter=new Adapter();using var c=new PreviewController(new[]{Entry("class",adapter,PreviewMode.Selection)},()=>new PreviewContext(false,true));
            Assert.That(c.ExecuteAsync("start","class","a").Result,Does.Contain("正式运行环境"));Assert.That(adapter.Mutations,Is.Zero);
        }
        [Test] public void SelectionIsValidatedBeforeTheExistingSessionCanChange()
        {
            var adapter=new Adapter();using var c=new PreviewController(new[]{Entry("class",adapter,PreviewMode.Selection)},()=>new PreviewContext(true,true));
            c.ExecuteAsync("on","class","a").GetAwaiter().GetResult();c.ExecuteAsync("start","class","missing").GetAwaiter().GetResult();Assert.That(adapter.Mutations,Is.Zero);
            c.ExecuteAsync("start","class","a").GetAwaiter().GetResult();Assert.That(adapter.Mutations,Is.EqualTo(1));
            adapter.Choices=new[]{new PreviewChoice("a","A"),new PreviewChoice("A","duplicate")};
            c.ExecuteAsync("start","class","a").GetAwaiter().GetResult();Assert.That(adapter.Mutations,Is.EqualTo(1));
        }
        [Test] public async Task ResetDuringPendingStartBlocksReplacementAndCleansTheLateCompletion()
        {
            var adapter=new Adapter{Pending=new TaskCompletionSource<string>()};using var c=new PreviewController(new[]{Entry("ui",adapter)},()=>new PreviewContext(true,true));
            var start=c.ExecuteAsync("on","ui");c.Reset(PreviewResetReason.PlayModeExit);
            Assert.That(await c.ExecuteAsync("on","ui"),Does.Contain("尚未收尾"));
            adapter.Pending.SetResult("late");Assert.That(await start,Does.Contain("取消"));Assert.That(adapter.Resets,Is.EqualTo(2));Assert.That(c.IsApplying,Is.False);
        }
        [Test] public void FailedStartCleansAndResetContinuesAfterOneAdapterThrows()
        {
            var a=new Adapter{Throw=true};var b=new Adapter();var c=new PreviewController(new[]{Entry("a",a),Entry("b",b)},()=>new PreviewContext(true,true));
            Assert.Throws<InvalidOperationException>(()=>c.ExecuteAsync("on","a").GetAwaiter().GetResult());Assert.That(a.Resets,Is.EqualTo(1));
            a.ThrowReset=true;Assert.Throws<AggregateException>(()=>c.Reset(PreviewResetReason.AssemblyReload));Assert.That(b.Resets,Is.EqualTo(1));
            a.ThrowReset=false;c.Dispose();
        }
        [Test] public void AdoptionPlanDoesNotExecuteOrClaimUndeclaredBindingsAreReady()
        {
            var a=new Adapter();string plan=PreviewAdoptionPlan.Describe(new[]{Entry("ui",a)});
            Assert.That(plan,Does.Contain("待声明"));Assert.That(plan,Does.Contain("只读"));Assert.That(a.Mutations,Is.Zero);
        }
        [Test] public void RuntimeGateUsesExplicitProductionDefaultAndIndependentPreviewOverrides()
        {
            string id="test-"+Guid.NewGuid().ToString("N");var a=PreviewRuntime.Feature(id);var b=PreviewRuntime.Feature(id+"-other");
            Assert.That(PreviewRuntime.Feature(id),Is.SameAs(a));Assert.That(a.IsEnabled,Is.False);a.SetEnabled(true);Assert.That(a.IsEnabled,Is.True);Assert.That(b.IsEnabled,Is.False);
            a.SetEnabled(false);Assert.That(a.IsEnabled,Is.False);
            Assert.Throws<InvalidOperationException>(()=>PreviewRuntime.Feature(id,true));
        }
    }
}
