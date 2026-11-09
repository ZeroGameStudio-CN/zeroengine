using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZeroEngine.Preview
{
    /// <summary>A reviewable declaration. It never edits source, adopts content or changes release status.</summary>
    public static class PreviewAdoptionPlan
    {
        public static string Describe(IEnumerable<PreviewRegistration> entries)
        {
            var text=new StringBuilder("正式采用与清理清单（只读；不修改正式配置）\n");
            foreach(var entry in entries??throw new ArgumentNullException(nameof(entries)))
            {
                var d=entry.Definition;text.AppendLine("["+d.Id+"] "+d.Title);
                text.AppendLine("正式接入："+(string.IsNullOrEmpty(d.ProductionBinding)?"待声明，不能据此直接转正。":d.ProductionBinding));
                Append("保留",d.PermanentPaths);Append("仅开发期接入",d.DevelopmentPaths);Append("采用前核对",d.AdoptionChecks);
                if(d.PermanentPaths.Count==0||d.AdoptionChecks.Count==0)text.AppendLine("采用边界尚未完整声明。");
            }
            return text.ToString();
            void Append(string label,IReadOnlyList<string> values){foreach(string value in values)text.AppendLine(label+"："+value);}
        }
    }
}
