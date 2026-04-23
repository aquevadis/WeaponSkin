using Microsoft.Extensions.Logging;
using Sharp.Shared.Enums;
using Sharp.Shared.GameEntities;
using Sharp.Shared.GameObjects;
using Sharp.Shared.HookParams;
using Sharp.Shared.Types;
using Sharp.Shared.Units;
using WeaponSkin.Managers;

namespace WeaponSkin.Modules;

internal partial class WeaponSkin : IModule
{
    private readonly InterfaceBridge    _bridge;
    private readonly IPlayerInfoManager _playerInfo;

    private readonly ILogger<WeaponSkin> _logger;

    private static uint _itemId = 16384;

    // ReSharper disable InconsistentNaming
    private readonly int CEconItemView_m_NetworkedDynamicAttributesOffset;

    private readonly unsafe delegate* unmanaged<nint, byte*, float, void> CAttributeList_SetOrAddAttributeValueByName;

    // ReSharper restore InconsistentNaming

    public WeaponSkin(InterfaceBridge bridge, IPlayerInfoManager playerInfo, ILogger<WeaponSkin> logger)
    {
        _bridge     = bridge;
        _playerInfo = playerInfo;
        _logger     = logger;

        CEconItemView_m_NetworkedDynamicAttributesOffset
            = bridge.SchemaManager.GetNetVarOffset("CEconItemView", "m_NetworkedDynamicAttributes");

        unsafe
        {
            CAttributeList_SetOrAddAttributeValueByName
                = (delegate* unmanaged<IntPtr, byte*, float, void>) bridge.ModSharp.GetGameData()
                                                                          .GetAddress("CAttributeList::SetOrAddAttributeValueByName");
        }
    }

    public bool Init()
    {
        _bridge.HookManager.GiveNamedItem.InstallHookPre(OnGiveNamedItemPre);
        _bridge.HookManager.GiveNamedItem.InstallHookPost(OnGiveNamedItemPost);
        _bridge.HookManager.PlayerKilledPost.InstallForward(OnPlayerKilledPost);

        return true;
    }

    public void Shutdown()
    {
        _bridge.HookManager.GiveNamedItem.RemoveHookPre(OnGiveNamedItemPre);
        _bridge.HookManager.GiveNamedItem.RemoveHookPost(OnGiveNamedItemPost);
        _bridge.HookManager.PlayerKilledPost.RemoveForward(OnPlayerKilledPost);
    }

    private HookReturnValue<IBaseWeapon> OnGiveNamedItemPre(IGiveNamedItemHookParams @params, HookReturnValue<IBaseWeapon> ret)
    {
        var client = @params.Client;

        if (client.IsFakeClient)
        {
            return new ();
        }

        var pawn = @params.Pawn;
        var team = pawn.Team;

        if (team <= CStrikeTeam.Spectator)
        {
            return new (EHookAction.SkipCallReturnOverride);
        }

        var classname = @params.Classname;

        if (classname.StartsWith("weapon_knife")
            && _playerInfo.GetPlayerKnife(client, team) is { } itemId
            && _bridge.EconItemManager.GetEconItemDefinitionByIndex(itemId) is { } definition)
        {
            @params.SetOverride(definition.DefinitionName, true);

            return new (EHookAction.ChangeParamReturnDefault);
        }

        return new ();
    }

    private void OnGiveNamedItemPost(IGiveNamedItemHookParams @params, HookReturnValue<IBaseWeapon> ret)
    {
        if (ret.Action == EHookAction.SkipCallReturnOverride)
        {
            return;
        }

        if (ret.ReturnValue is not { IsWeapon: true } weapon)
        {
            return;
        }

        var client = @params.Client;

        if (client.IsFakeClient)
        {
            return;
        }

        var view      = weapon.AttributeContainer.Item;
        var itemIndex = weapon.ItemDefinitionIndex;

        if (weapon.IsKnife
            && ret.Action                 != EHookAction.ChangeParamReturnDefault
            && weapon.ItemDefinitionIndex != (int) EconItemId.KnifeCt
            && weapon.ItemDefinitionIndex != (int) EconItemId.KnifeTe)
        {
            view.SetNetVar("m_iEntityQuality", 4);
            view.SetQualityLocal(4);
        }

        if (_playerInfo.GetPlayerWeaponSkin(client, (EconItemId) itemIndex) is not { } skin)
        {
            return;
        }

        view.SetAccountIdLocal(client.SteamId.AccountId);
        view.SetItemIdLowLocal(uint.MaxValue);
        view.SetItemIdHighLocal(_itemId++);

        if (!string.IsNullOrEmpty(skin.NameTag))
        {
            view.SetCustomNameLocal(skin.NameTag);
        }

        if (skin.StatTrak is { } statTrak)
        {
            view.SetQualityLocal(9);
            SetOrAddAttribute(view, "kill eater"u8,            statTrak);
            SetOrAddAttribute(view, "kill eater score type"u8, 0);
        }

        if (_bridge.EconItemManager.GetPaintKits().TryGetValue(skin.PaintId, out var paintKit))
        {
            SetOrAddAttribute(view, "set item texture prefab"u8, skin.PaintId);
            SetOrAddAttribute(view, "set item texture wear"u8, skin.Wear);
            SetOrAddAttribute(view, "set item texture seed"u8, skin.Seed);

            if (weapon.Slot is GearSlot.Rifle or GearSlot.Pistol && paintKit.IsLegacyModel)
            {
                weapon.SetBodyGroupByName("body", 1);
            }
        }

        for (var i = 0; i < skin.Stickers.Length; i++)
        {
            var sticker = skin.Stickers[i];

            if (sticker is null)
            {
                continue;
            }

            var schema = GetStickerSchema(i);
            SetOrAddAttribute(view, schema.Id, BitConverter.Int32BitsToSingle(sticker.StickerId));
            SetOrAddAttribute(view, schema.Wear, sticker.Wear);

            SetOrAddAttribute(view, schema.Scale, sticker.Scale);
            SetOrAddAttribute(view, schema.Rotation, sticker.Rotation);
            SetOrAddAttribute(view, schema.OffsetX, sticker.OffsetX);
            SetOrAddAttribute(view, schema.OffsetY, sticker.OffsetY);
        }

        if (skin.Keychain is { } keychain)
        {
            var keychainSchema = GetKeychainSchema(0);
            SetOrAddAttribute(view, keychainSchema.Id, BitConverter.Int32BitsToSingle(keychain.KeychainId));
            SetOrAddAttribute(view, keychainSchema.Seed, keychain.Seed);
            SetOrAddAttribute(view, keychainSchema.OffsetX, keychain.X);
            SetOrAddAttribute(view, keychainSchema.OffsetY, keychain.Y);
            SetOrAddAttribute(view, keychainSchema.OffsetZ, keychain.Z);
        }
    }

    private void OnPlayerKilledPost(IPlayerKilledForwardParams @params)
    {
        var attackerSlot = @params.AttackerPlayerSlot;

        if (_bridge.EntityManager.FindEntityByHandle(@params.AttackerPawnHandle) is not { } attackerPawn
            || !attackerPawn.IsPlayer(true)
            || attackerSlot < 0
            || _bridge.ClientManager.GetGameClient((PlayerSlot) attackerSlot) is not { } attackerClient)
        {
            return;
        }

        var attackEntity = _bridge.EntityManager.FindEntityByHandle(@params.AbilityHandle);

        if (attackEntity?.AsBaseWeapon() is not { } weapon)
        {
            return;
        }

        var econItemIndex = (EconItemId) weapon.ItemDefinitionIndex;

        if (_playerInfo.GetPlayerWeaponSkin(attackerClient, econItemIndex) is not { } skin)
        {
            return;
        }

        if (skin.StatTrak is null)
        {
            return;
        }

        var view     = weapon.AttributeContainer.Item;
        var statTrak = skin.StatTrak.Value + 1;

        skin.StatTrak = statTrak;
        view.SetQualityLocal(9);
        SetOrAddAttribute(view, "kill eater"u8, statTrak);
        SetOrAddAttribute(view, "kill eater score type"u8, 0);

        var steamId = attackerClient.SteamId;

        Task.Run(async () =>
        {
            try
            {
                if (_bridge.GetRequestManager() is { } request)
                {
                    await request.UpdateStatTrak(steamId, econItemIndex, statTrak).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update StatTrak for {steamId}", steamId);
            }
        });
    }

    private unsafe void SetOrAddAttribute(IEconItemView view, ReadOnlySpan<byte> name, float value)
    {
        fixed (byte* ptr = name)
        {
            CAttributeList_SetOrAddAttributeValueByName(view.GetAbsPtr() + CEconItemView_m_NetworkedDynamicAttributesOffset,
                                                        ptr,
                                                        value);
        }
    }
}
