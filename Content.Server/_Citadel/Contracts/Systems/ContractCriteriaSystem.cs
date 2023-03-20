﻿using Content.Server._Citadel.Contracts.Components;
using JetBrains.Annotations;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.Server._Citadel.Contracts.Systems;

/// <summary>
/// This handles contract criteria, their setup, and querying them.
/// </summary>
[PublicAPI]
public sealed class ContractCriteriaSystem : EntitySystem
{
    [Dependency] private readonly ContractManagementSystem _contract = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<ContractCriteriaControlComponent, ContractStatusChangedEvent>(OnContractStatusChanged);
        SubscribeLocalEvent<ContractComponent, CriteriaUpdatedEvent>(OnCriteriaUpdated);
    }

    private void OnCriteriaUpdated(EntityUid uid, ContractComponent component, CriteriaUpdatedEvent args)
    {
        if (component.Status != ContractStatus.Active)
            return;

        if (!TryComp<ContractCriteriaControlComponent>(uid, out var criteriaControl))
            return;

        // Deliberately process finalizing first, prefer letting them succeed over failure.
        var allPass = criteriaControl.FinalizingCriteria.Count > 0; // Auto-fail if no finalizing criteria exist.
        foreach (var criterion in criteriaControl.FinalizingCriteria)
        {
            var criteria = Comp<ContractCriteriaComponent>(criterion);
            allPass &= criteria.Satisfied;
        }

        if (allPass)
        {
            _contract.TryFinalizeContract(uid, component);
            return;
        }

        foreach (var criterion in criteriaControl.BreachingCriteria)
        {
            var criteria = Comp<ContractCriteriaComponent>(criterion);
            if (criteria.Satisfied)
            {
                _contract.TryBreachContract(uid, component);
                return;
            }
        }
    }

    #region Event Handling
    private void OnContractStatusChanged(EntityUid contractUid, ContractCriteriaControlComponent criteriaControlComponent, ContractStatusChangedEvent args)
    {
        switch (args)
        {
            // Contract initiated, set up the criteria.
            case (ContractStatus.Uninitialized, ContractStatus.Initiating):
            {
                OnContractInitiating(contractUid, criteriaControlComponent);
                break;
            }
            // Contract active, set up criteria checking.
            case (ContractStatus.Initiating, ContractStatus.Active):
            {
                OnContractActive(contractUid, criteriaControlComponent);
                break;
            }
        }
    }

    private void OnContractInitiating(EntityUid contractUid, ContractCriteriaControlComponent criteriaControlComponent)
    {
        foreach (var proto in criteriaControlComponent.ContractBreachingCriteriaPrototypes)
        {
            AddContractBreachingCriteria(contractUid, proto, criteriaControlComponent);
        }

        foreach (var proto in criteriaControlComponent.ContractFinalizingCriteriaPrototypes)
        {
            AddContractFinalizingCriteria(contractUid, proto, criteriaControlComponent);
        }
    }

    private void OnContractActive(EntityUid contractUid, ContractCriteriaControlComponent criteriaControlComponent)
    {
        foreach (var criteria in criteriaControlComponent.BreachingCriteria)
        {
            RaiseLocalEvent(criteria, new CriteriaStartTickingEvent());
        }

        foreach (var criteria in criteriaControlComponent.FinalizingCriteria)
        {
            RaiseLocalEvent(criteria, new CriteriaStartTickingEvent());
        }
    }
    #endregion

    private EntityUid BuildContractCriteria(EntityUid contractUid, string criteriaProto,
        ContractCriteriaControlComponent? criteriaControlComponent = null)
    {
        var criteriaEnt = Spawn(criteriaProto, MapCoordinates.Nullspace);
        DebugTools.Assert(HasComp<ContractCriteriaComponent>(criteriaEnt), $"The contract {criteriaProto} is missing required components!");

        var criteria = Comp<ContractCriteriaComponent>(criteriaEnt);
        criteria.OwningContract = contractUid;

        RaiseLocalEvent(criteriaEnt, new CriteriaSetupEvent());

        return criteriaEnt;
    }

    /// <summary>
    /// Adds a finalizing criteria to a contract.
    /// </summary>
    /// <returns>An initialized criteria.</returns>
    public EntityUid AddContractFinalizingCriteria(EntityUid contractUid, string criteriaProto,
        ContractCriteriaControlComponent? criteriaControlComponent = null)
    {
        // Deliberate throw-if-not-present, hence not using Resolve.
        criteriaControlComponent ??= Comp<ContractCriteriaControlComponent>(contractUid);

        var criteria = BuildContractCriteria(contractUid, criteriaProto, criteriaControlComponent);
        criteriaControlComponent.FinalizingCriteria.Add(criteria);

        return criteria;
    }

    /// <summary>
    /// Adds a breaching criteria to a contract.
    /// </summary>
    /// <returns>An initialized criteria.</returns>
    public EntityUid AddContractBreachingCriteria(EntityUid contractUid, string criteriaProto,
        ContractCriteriaControlComponent? criteriaControlComponent = null)
    {
        // Deliberate throw-if-not-present, hence not using Resolve.
        criteriaControlComponent ??= Comp<ContractCriteriaControlComponent>(contractUid);

        var criteria = BuildContractCriteria(contractUid, criteriaProto, criteriaControlComponent);
        criteriaControlComponent.BreachingCriteria.Add(criteria);

        return criteria;
    }
}
