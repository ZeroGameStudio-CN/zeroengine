using System;
using System.Reflection;
using UnityEngine;

#if ODIN_INSPECTOR
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
#endif

namespace ZGS.DataToolkit.Editor
{
#if ODIN_INSPECTOR
    public sealed class DataAuthoringOdinLockAttributeProcessor : OdinAttributeProcessor
    {
        public override bool CanProcessChildMemberAttributes(InspectorProperty parentProperty, MemberInfo member)
        {
            return TryGetLockedField(member, out _, out _);
        }

        public override void ProcessChildMemberAttributes(InspectorProperty property, MemberInfo member, List<Attribute> attributes)
        {
            if (!TryGetLockedField(member, out var fieldInfo, out var lockedField))
            {
                return;
            }

            var disableExpression = DataAuthoringFieldLockUtility.BuildAssignedValueDisableExpression(
                fieldInfo.Name,
                fieldInfo.FieldType,
                isLocked: true);
            if (!string.IsNullOrWhiteSpace(disableExpression)
                && !ContainsAttribute<DisableIfAttribute>(attributes))
            {
                attributes.Add(new DisableIfAttribute(disableExpression));
            }

            if (!ContainsAttribute<LabelTextAttribute>(attributes))
            {
                attributes.Add(new LabelTextAttribute(lockedField.DisplayName));
            }

            if (!string.IsNullOrWhiteSpace(lockedField.Reason)
                && !ContainsAttribute<TooltipAttribute>(attributes))
            {
                attributes.Add(new TooltipAttribute(lockedField.Reason));
            }
        }

        private static bool TryGetLockedField(
            MemberInfo member,
            out FieldInfo fieldInfo,
            out DataAuthoringLockedField lockedField)
        {
            fieldInfo = member as FieldInfo;
            lockedField = null;
            return fieldInfo?.DeclaringType != null
                && DataAuthoringFieldLockRegistry.TryGetLockedField(fieldInfo.DeclaringType, fieldInfo.Name, out lockedField);
        }

        private static bool ContainsAttribute<TAttribute>(List<Attribute> attributes)
            where TAttribute : Attribute
        {
            foreach (var attribute in attributes)
            {
                if (attribute is TAttribute)
                {
                    return true;
                }
            }

            return false;
        }
    }
#endif
}
