using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ZeroEngine.Preview
{
    public enum PreviewMode { Toggle, Selection, Information }
    public enum PreviewOrigin { Console, Dashboard, Automation }
    public enum PreviewResetReason { PlayModeExit, AssemblyReload, Dispose }

    public readonly struct PreviewContext
    {
        public readonly bool IsDevelopment, IsPlaying, IsTransitioning;
        public PreviewContext(bool isDevelopment, bool isPlaying, bool isTransitioning = false)
        { IsDevelopment=isDevelopment; IsPlaying=isPlaying; IsTransitioning=isTransitioning; }
    }

    public sealed class PreviewChoice
    {
        public string Id { get; }
        public string Title { get; }
        public PreviewChoice(string id,string title)
        { if(string.IsNullOrWhiteSpace(id))throw new ArgumentException(nameof(id));Id=id;Title=title??id; }
    }

    public sealed class PreviewDefinition
    {
        public string Id { get; }
        public string Title { get; }
        public string Group { get; }
        public string Rule { get; }
        public string Entrance { get; }
        public PreviewMode Mode { get; }
        public string ProductionBinding { get; }
        public IReadOnlyList<string> PermanentPaths { get; }
        public IReadOnlyList<string> DevelopmentPaths { get; }
        public IReadOnlyList<string> AdoptionChecks { get; }
        public PreviewDefinition(string id,string title,string group,string rule,string entrance,PreviewMode mode,
            string productionBinding="",IEnumerable<string> permanentPaths=null,IEnumerable<string> developmentPaths=null,IEnumerable<string> adoptionChecks=null)
        {
            ValidateId(id);
            if(!Enum.IsDefined(typeof(PreviewMode),mode))throw new ArgumentOutOfRangeException(nameof(mode));
            Id=id;Title=title??id;Group=group??"";Rule=rule??"";Entrance=entrance??"";Mode=mode;
            ProductionBinding=productionBinding??"";
            PermanentPaths=Copy(permanentPaths);DevelopmentPaths=Copy(developmentPaths);AdoptionChecks=Copy(adoptionChecks);
        }
        private static IReadOnlyList<string> Copy(IEnumerable<string> values)=>Array.AsReadOnly((values??Array.Empty<string>()).Where(s=>!string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).ToArray());
        public static void ValidateId(string id)
        {
            if(id=="all")throw new ArgumentException("The preview ID 'all' is reserved for read-only group queries.",nameof(id));
            if(string.IsNullOrEmpty(id)||id.Length>128||id.Any(c=>!(c>='a'&&c<='z'||c>='0'&&c<='9'||c=='.'||c=='-'||c=='_')))
                throw new ArgumentException("Preview IDs use lowercase letters, digits, dot, underscore and hyphen.",nameof(id));
        }
    }

    public readonly struct PreviewRequest
    {
        public readonly string Action, Selection;
        public readonly PreviewOrigin Origin;
        public PreviewRequest(string action,string selection="",PreviewOrigin origin=PreviewOrigin.Console)
        {Action=(action??"status").Trim().ToLowerInvariant();Selection=selection??"";Origin=origin;}
    }

    public interface IPreviewAdapter
    {
        string ReadStatus();
        IReadOnlyList<PreviewChoice> Choices { get; }
        bool CanExecute(PreviewRequest request,PreviewContext context,out string reason);
        Task<string> ExecuteAsync(PreviewRequest request,PreviewContext context);
        void Reset(PreviewResetReason reason);
    }

    public sealed class PreviewRegistration
    {
        public PreviewDefinition Definition { get; }
        public IPreviewAdapter Adapter { get; }
        public PreviewRegistration(PreviewDefinition definition,IPreviewAdapter adapter)
        {Definition=definition??throw new ArgumentNullException(nameof(definition));Adapter=adapter??throw new ArgumentNullException(nameof(adapter));}
    }
}
