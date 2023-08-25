﻿using System.Linq;
using Content.Server._Citadel.Contracts.Components;
using Content.Server._Citadel.Contracts.Systems;
using Content.Server._Citadel.PDAContracts.Components;
using Content.Server.CartridgeLoader;
using Content.Server.Players;
using Content.Shared._Citadel.Contracts;
using Content.Shared._Citadel.Contracts.BUI;
using Content.Shared.CartridgeLoader;
using Robust.Server.Player;
using Robust.Shared.Utility;

namespace Content.Server._Citadel.PDAContracts.Systems;

/// <summary>
/// This handles the contracts cartridge and it's UI.
/// </summary>
public sealed class ContractsCartridgeSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem? _cartridgeLoaderSystem = default!;
    [Dependency] private readonly ContractCriteriaSystem _criteria = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<ContractsCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
    }

    private void OnUiReady(EntityUid uid, ContractsCartridgeComponent component, CartridgeUiReadyEvent args)
    {
        UpdateUiState(uid, args.Loader, (IPlayerSession)args.Session, component);
    }

    public ContractListUiState GenerateState(EntityUid cart, IPlayerSession user)
    {
        var mind = user.GetMind()!;
        var conQuery = EntityQueryEnumerator<ContractComponent, ContractCriteriaControlComponent>();

        var contractStates = new Dictionary<Guid, ContractUiState>();

        while (conQuery.MoveNext(out var contract, out var contractComp, out var criteriaComp))
        {
            var status = ContractUiState.ContractUserStatus.OpenToJoin;
            var subCons = contractComp.SubContractors;
            if (contractComp.OwningContractor is null)
            {
                status = ContractUiState.ContractUserStatus.OpenToOwn;
            }
            else if (subCons.Contains(mind))
            {
                status = ContractUiState.ContractUserStatus.Subcontractor;
            }
            else if (contractComp.OwningContractor == mind)
            {
                status = ContractUiState.ContractUserStatus.Owner;
            }

            if (contractComp.Status is ContractStatus.Finalized or ContractStatus.Breached && status == ContractUiState.ContractUserStatus.OpenToJoin)
                continue;

            var ownerName = contractComp.OwningContractor?.CharacterName ?? "[INACTIVE]";
            var subContractorNames = contractComp.SubContractors.Select(x => x.CharacterName!).ToList();
            var state = new ContractUiState(status, Name(contract), ownerName, subContractorNames,
                new ContractDisplayData(FormattedMessage.FromUnformatted(Description(contract))), contractComp.Status);

            foreach (var (group, criteria) in criteriaComp.Criteria)
            {
                state.Criteria[group] = new();
                state.Effects[group] = new();
                var list = state.Criteria[group];
                var effects = state.Effects[group];
                foreach (var criterion in criteria)
                {
                    if (_criteria.TryGetCriteriaDisplayData(criterion, out var data))
                        list.Add(data.Value);
                }

                var criteriaEffects = criteriaComp.CriteriaEffects;
                if (!criteriaEffects.ContainsKey(group))
                    continue;

                foreach (var effect in criteriaComp.CriteriaEffects[group])
                {
                    if (effect.Describe() is {} desc)
                        effects.Add(FormattedMessage.FromMarkup(desc));
                }
            }

            contractStates.Add(contractComp.Uuid, state);
        }

        return new ContractListUiState(contractStates);
    }

    private void UpdateUiState(EntityUid uid, EntityUid loaderUid, IPlayerSession session, ContractsCartridgeComponent? component)
    {
        if (!Resolve(uid, ref component))
            return;

        var innerState = GenerateState(uid, session);

        _cartridgeLoaderSystem?.UpdateCartridgeUiState(loaderUid, new ContractCartridgeUiState(innerState));
    }
}
