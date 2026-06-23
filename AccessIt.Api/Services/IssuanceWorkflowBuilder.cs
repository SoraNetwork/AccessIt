using AccessIt.Api.Domain;

namespace AccessIt.Api.Services;

public static class IssuanceWorkflowBuilder
{
    public static IReadOnlyList<IssuanceStepType> BuildUpsertSteps(AccessDevice device, bool hasCard, bool hasFace, bool hasPassword)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!device.SupportsUserInfo)
            throw new InvalidOperationException("The selected device does not support access people.");

        var steps = new List<IssuanceStepType>();
        if (device.SupportsUserRightPlanTemplate && (device.AllDayTemplateId == null || !device.HasAllDayTemplate))
            steps.Add(IssuanceStepType.EnsureAllDayTemplate);

        steps.Add(IssuanceStepType.UpsertUser);
        if (hasCard && device.SupportsCardInfo)
            steps.Add(IssuanceStepType.UpsertCard);
        if (hasFace && device.SupportsFace)
            steps.Add(IssuanceStepType.UpsertFace);
        return steps;
    }

    public static IReadOnlyList<IssuanceStepType> BuildDeleteSteps(bool hasCard, bool hasFace)
    {
        var steps = new List<IssuanceStepType>();
        if (hasFace) steps.Add(IssuanceStepType.DeleteFace);
        if (hasCard) steps.Add(IssuanceStepType.DeleteCard);
        steps.Add(IssuanceStepType.DeleteUser);
        return steps;
    }
}
