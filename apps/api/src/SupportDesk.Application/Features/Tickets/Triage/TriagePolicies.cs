using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets.Triage;

public sealed class SlaOptions
{
    public const string SectionName = "Sla";
    public Dictionary<string, double> BaseWindowHours { get; init; } = [];
    public double PremiumCustomerMultiplier { get; init; } = 0.5;
    public double MinimumWindowHours { get; init; } = 1;
    public double AtRiskThresholdPercent { get; init; } = 25;
}

public sealed record SlaDecision(TimeSpan Window, int WindowMinutes, string Reason);

public sealed class SlaPolicy(SlaOptions options)
{
    public SlaOptions Options => options;

    public SlaDecision Decide(TicketPriority priority, CustomerTier tier)
    {
        if (!options.BaseWindowHours.TryGetValue(priority.ToString(), out var baseHours))
        {
            throw new InvalidOperationException($"SLA window for {priority} is not configured.");
        }

        var adjustedHours = tier == CustomerTier.Premium
            ? baseHours * options.PremiumCustomerMultiplier
            : baseHours;
        var hours = Math.Max(adjustedHours, options.MinimumWindowHours);
        var window = TimeSpan.FromHours(hours);
        var minutes = (int)Math.Round(window.TotalMinutes, MidpointRounding.AwayFromZero);
        var reason = tier == CustomerTier.Premium
            ? $"{priority} uses {baseHours:g} hours; Premium reduces it to {hours:g} hours."
            : $"{priority} uses the configured {hours:g}-hour response window.";
        return new SlaDecision(window, minutes, reason);
    }
}

public sealed record PriorityDecision(TicketPriority Priority, string Reason);

public static class PriorityPolicy
{
    public static PriorityDecision Decide(TicketPriority? requested, Category category)
    {
        if (category.ForcesCriticalPriority)
        {
            return new PriorityDecision(TicketPriority.Critical,
                $"Category {category.Name} forces Critical priority.");
        }

        return requested is { } priority
            ? new PriorityDecision(priority, $"Applied the requested {priority} priority.")
            : new PriorityDecision(TicketPriority.Medium, "No priority was requested, so Medium was applied.");
    }
}

public sealed record AssignmentDecision(int? AgentId, string? AgentName, string Reason);

public sealed class AssignmentPolicy(IAgentRepository agents)
{
    public async Task<AssignmentDecision> DecideAsync(Category category, int? currentAgentId, CancellationToken ct)
    {
        var candidates = await agents.GetAssignmentCandidatesAsync(
            category.Id,
            category.RequiresSpecialist,
            ct);

        if (currentAgentId is not null && candidates.FirstOrDefault(x => x.Id == currentAgentId) is { } current)
        {
            return new AssignmentDecision(current.Id, current.FullName,
                $"Kept {current.FullName}, who remains eligible.");
        }

        var selected = candidates.FirstOrDefault();
        if (selected is null)
        {
            var specialist = category.RequiresSpecialist ? " specialist" : string.Empty;
            return new AssignmentDecision(null, null,
                $"No active{specialist} agent is below their open-ticket limit; ticket left unassigned.");
        }

        return new AssignmentDecision(selected.Id, selected.FullName,
            $"Assigned to {selected.FullName}, with {selected.OpenTicketCount} open ticket(s), using agent ID as the tie-break.");
    }
}
