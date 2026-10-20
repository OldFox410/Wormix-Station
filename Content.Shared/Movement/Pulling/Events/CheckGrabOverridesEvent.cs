// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Common.Grab;
using Content.Goobstation.Common.MartialArts;

namespace Content.Shared.Movement.Pulling.Events;

public sealed class CheckGrabOverridesEvent : EntityEventArgs
{
    public CheckGrabOverridesEvent(GrabStage stage, EntityUid target)
    {
        Stage = stage;
        Target = target; // EDIT Wormix
    }

    public GrabStage Stage { get; set; }

    public EntityUid Target { get; set; } // EDIT Wormix
}
