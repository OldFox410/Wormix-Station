using Content.Goobstation.Common.Grab;
using Content.Goobstation.Common.MartialArts;
using Content.Goobstation.Shared.GrabIntent;
using Content.Goobstation.Shared.MartialArts.Components;
using Content.Goobstation.Shared.MartialArts.Events;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Projectiles;
using Content.Shared.Speech;
using Content.Shared.Standing;
using Content.Shared.Tag;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Audio;
using Robust.Shared.Physics.Events;
using Robust.Shared.Prototypes;
using System.Linq;

namespace Content.Goobstation.Shared.MartialArts;

public partial class SharedMartialArtsSystem
{
    private void InitializeCombatives()
    {
        // Combo Subscribers
        SubscribeLocalEvent<CanPerformComboComponent, CombativesRestrainPerformedEvent>(OnCombativesRestrain);
        SubscribeLocalEvent<CanPerformComboComponent, CombativesThrowPerformedEvent>(OnCombativesThrow);
        SubscribeLocalEvent<CanPerformComboComponent, CombativesChokePerformedEvent>(OnCombativesChoke);
        SubscribeLocalEvent<CanPerformComboComponent, CombativesSlitThroatPerformedEvent>(OnCombativesSlitThroat);
        SubscribeLocalEvent<CanPerformComboComponent, CombativesKnockdownPerformedEvent>(OnCombativesKnockdown);
        SubscribeLocalEvent<CanPerformComboComponent, CombativesWeakeningPerformedEvent>(OnCombativesWeakening);
        SubscribeLocalEvent<CanPerformComboComponent, CombativesPummelPerformedEvent>(OnCombativesPummel);
        SubscribeLocalEvent<CanPerformComboComponent, CombativesDisarmPerformedEvent>(OnCombativesDisarm);

        // Utility & Item Grant
        SubscribeLocalEvent<GrantCombativesComponent, UseInHandEvent>(OnGrantCombativesUse);
        SubscribeLocalEvent<GrantCombativesComponent, MapInitEvent>(OnGrantCombativesMapInit);

        // RestrainComponent Subscribers
        SubscribeLocalEvent<CombativesRestrainComponent, InteractionAttemptEvent>(OnRestrainCancelInteraction);
        SubscribeLocalEvent<CombativesRestrainComponent, UseAttemptEvent>(OnRestrainCancelUse);
        SubscribeLocalEvent<CombativesRestrainComponent, PickupAttemptEvent>(OnRestrainCancelPickup);
        SubscribeLocalEvent<CombativesRestrainComponent, BeforeReleaseEvent>(OnRestrainReleaseAttempt);
        SubscribeLocalEvent<CombativesRestrainComponent, AttackAttemptEvent>(OnRestrainAttackAttempt);
        SubscribeLocalEvent<CombativesRestrainComponent, SpeakAttemptEvent>(OnRestrainSpeakAttempt);
        SubscribeLocalEvent<CombativesRestrainComponent, PreventCollideEvent>(OnRestrainPreventCollide);

        SubscribeLocalEvent<CombativesRestrainComponent, StoodEvent>(OnRestrainStood);
        SubscribeLocalEvent<CombativesRestrainComponent, PullStoppedMessage>(OnRestrainStopped);
    }

    private static readonly ProtoId<TagPrototype>[] AllowedTags = ["CombatKnife"];

    #region Generic Methods

    private void OnGrantCombativesMapInit(Entity<GrantCombativesComponent> ent, ref MapInitEvent args)
    {
        if (!HasComp<MobStateComponent>(ent))
            return;

        TryGrantMartialArt(ent, ent.Comp);
    }

    private void OnGrantCombativesUse(EntityUid ent, GrantCombativesComponent comp, UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (!_netManager.IsServer)
            return;

        if (!TryGrantMartialArt(args.User, comp))
            return;

        var coords = Transform(args.User).Coordinates;
        _audio.PlayPvs(comp.SoundOnUse, coords);

        if (comp.MultiUse)
            return;

        QueueDel(ent);
        if (comp.SpawnedProto != null)
            Spawn(comp.SpawnedProto, coords);
    }

    private void OnCombativesAttackPerformed(Entity<MartialArtsKnowledgeComponent> ent, ref ComboAttackPerformedEvent args)
    {
        if (args.Weapon != args.Performer || args.Target == args.Performer)
            return;

        switch (args.Type)
        {
            case ComboAttackType.Grab:
                if (!TryComp<PullerComponent>(ent, out var puller)
                    || !TryComp<GrabIntentComponent>(ent, out var grabIntent)
                    || !TryComp<PullableComponent>(args.Target, out var pullable)
                    || !TryComp<GrabbableComponent>(args.Target, out var grabbable))
                    return;
                grabbable.NextEscapeAttempt = _timing.CurTime.Add(TimeSpan.FromSeconds(2));
                break;
        }

    }

    private void OnCombativesMeleeAttackRate(Entity<MartialArtsKnowledgeComponent> ent, ref GetMeleeAttackRateEvent args)
    {
        if (args.User != args.Weapon)
            return;

        if (!TryComp<HandsComponent>(args.User, out var hands))
            return;

        var helding = _hands.EnumerateHeld((args.User, hands)).ToList();

        if (TryComp<PullerComponent>(args.User, out var puller)
            && HasComp<CombativesRestrainComponent>(puller.Pulling))
            return;

        args.Multipliers *= 3f;
    }

    #endregion

    #region Combo Methods

    private void OnCombativesRestrain(Entity<CanPerformComboComponent> ent, ref CombativesRestrainPerformedEvent args)
    {
        if (!_proto.TryIndex(ent.Comp.BeingPerformed, out var proto)
            || !TryUseMartialArt(ent, proto, out var target, out _))
            return;

        if (!HasComp<CombativesRestrainComponent>(target))
        {
            AddComp<CombativesRestrainComponent>(target).Puller = ent;
        }

        if (TryComp<HandsComponent>(target, out var hands))
        {
            foreach (var hand in hands.Hands.Keys)
            {
                _virtualItem.TrySpawnVirtualItemInHand(ent, target);
            }
        }

        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Weapons/genhit2.ogg"), target);
        ComboPopup(ent, target, proto.ID);
        ent.Comp.LastAttacks.Clear();
    }

    private void OnCombativesThrow(Entity<CanPerformComboComponent> ent, ref CombativesThrowPerformedEvent args)
    {
        if (!_proto.TryIndex(ent.Comp.BeingPerformed, out var proto)
            || !TryUseMartialArt(ent, proto, out var target, out _))
            return;

        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Weapons/genhit2.ogg"), target);
        ComboPopup(ent, target, proto.ID);
        ent.Comp.LastAttacks.Clear();
    }

    private void OnCombativesChoke(Entity<CanPerformComboComponent> ent, ref CombativesChokePerformedEvent args)
    {
        if (!_proto.TryIndex(ent.Comp.BeingPerformed, out var proto)
            || !TryUseMartialArt(ent, proto, out var target, out _))
            return;

        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Weapons/genhit2.ogg"), target);
        ComboPopup(ent, target, proto.ID);
        ent.Comp.LastAttacks.Clear();
    }

    private void OnCombativesSlitThroat(Entity<CanPerformComboComponent> ent, ref CombativesSlitThroatPerformedEvent args)
    {
        if (!_proto.TryIndex(ent.Comp.BeingPerformed, out var proto)
            || !TryUseMartialArt(ent, proto, out var target, out _))
            return;

        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Weapons/genhit2.ogg"), target);
        ComboPopup(ent, target, proto.ID);
        ent.Comp.LastAttacks.Clear();
    }

    private void OnCombativesKnockdown(Entity<CanPerformComboComponent> ent, ref CombativesKnockdownPerformedEvent args)
    {
        if (!_proto.TryIndex(ent.Comp.BeingPerformed, out var proto)
            || !TryUseMartialArt(ent, proto, out var target, out _))
            return;

        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Weapons/genhit2.ogg"), target);
        ComboPopup(ent, target, proto.ID);
        ent.Comp.LastAttacks.Clear();
    }

    private void OnCombativesWeakening(Entity<CanPerformComboComponent> ent, ref CombativesWeakeningPerformedEvent args)
    {
        if (!_proto.TryIndex(ent.Comp.BeingPerformed, out var proto)
            || !TryUseMartialArt(ent, proto, out var target, out _))
            return;

        // ВАЖНО: Не делаем ent.Comp.LastAttacks.Clear(), чтобы Disarm -> Disarm переходил в Pummel!
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Weapons/genhit2.ogg"), target);
        ComboPopup(ent, target, proto.ID);
    }

    private void OnCombativesPummel(Entity<CanPerformComboComponent> ent, ref CombativesPummelPerformedEvent args)
    {
        if (!_proto.TryIndex(ent.Comp.BeingPerformed, out var proto)
            || !TryUseMartialArt(ent, proto, out var target, out _))
            return;

        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Weapons/genhit2.ogg"), target);
        ComboPopup(ent, target, proto.ID);
        ent.Comp.LastAttacks.Clear();
    }

    private void OnCombativesDisarm(Entity<CanPerformComboComponent> ent, ref CombativesDisarmPerformedEvent args)
    {
        if (!_proto.TryIndex(ent.Comp.BeingPerformed, out var proto)
            || !TryUseMartialArt(ent, proto, out var target, out _))
            return;

        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Weapons/genhit2.ogg"), target);
        ComboPopup(ent, target, proto.ID);
        ent.Comp.LastAttacks.Clear();
    }

    #endregion

    #region Restrain

    private void OnRestrainCancelInteraction(Entity<CombativesRestrainComponent> ent, ref InteractionAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnRestrainCancelUse(Entity<CombativesRestrainComponent> ent, ref UseAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnRestrainCancelPickup(Entity<CombativesRestrainComponent> ent, ref PickupAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnRestrainReleaseAttempt(Entity<CombativesRestrainComponent> ent, ref BeforeReleaseEvent args)
    {
        args.Canceled = true;
    }

    private void OnRestrainAttackAttempt(Entity<CombativesRestrainComponent> ent, ref AttackAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnRestrainSpeakAttempt(Entity<CombativesRestrainComponent> ent, ref SpeakAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnRestrainPreventCollide(Entity<CombativesRestrainComponent> ent, ref PreventCollideEvent args)
    {
        // Проверяем, является ли другой объект коллизии снарядом
        if (!HasComp<AmmoComponent>(args.OtherEntity) && !HasComp<ProjectileComponent>(args.OtherEntity))
            return;

        // Достаем компонент снаряда, чтобы узнать, кто выстрелил
        if (!TryComp<ProjectileComponent>(args.OtherEntity, out var projectile))
            return;

        // Если выстрелил тот, кто держит в захвате (Puller) — пуля не задевает удерживаемую жертву
        if (projectile.Shooter == ent.Comp.Puller || projectile.Weapon == ent.Comp.Puller)
        {
            args.Cancelled = true;
        }
    }

    private void OnRestrainStood(Entity<CombativesRestrainComponent> ent, ref StoodEvent args)
    {
        if (!TryComp<PullableComponent>(ent, out var pullable))
            return;

        _virtualItem.DeleteInHandsMatching(ent, ent.Comp.Puller);
        _pulling.TryStopPull(ent, pullable, ent.Comp.Puller, true);
        RemComp<CombativesRestrainComponent>(ent);
    }

    private void OnRestrainStopped(Entity<CombativesRestrainComponent> ent, ref PullStoppedMessage args)
    {
        if (args.PullerUid != ent.Comp.Puller)
            return;

        _virtualItem.DeleteInHandsMatching(ent, ent.Comp.Puller);

        if (!_status.HasStatusEffect(ent, "Stun"))
            _status.TryRemoveStatusEffect(ent, "KnockedDown");

        RemComp<CombativesRestrainComponent>(ent);
    }


    #endregion
}
