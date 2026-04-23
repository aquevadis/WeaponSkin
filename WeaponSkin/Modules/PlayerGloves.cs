using Iced.Intel;
using Microsoft.Extensions.Logging;
using Sharp.Shared.Enums;
using Sharp.Shared.HookParams;
using WeaponSkin.Managers;

namespace WeaponSkin.Modules;

internal class PlayerGloves : IModule
{
    private readonly InterfaceBridge       _bridge;
    private readonly IPlayerInfoManager    _playerInfo;
    private readonly ILogger<PlayerGloves> _logger;

    private readonly unsafe delegate* unmanaged<nint, byte, void> CCSPlayerPawn_SetGlovesBodyGroup;

    public PlayerGloves(InterfaceBridge bridge, IPlayerInfoManager playerInfo, ILogger<PlayerGloves> logger)
    {
        _bridge     = bridge;
        _playerInfo = playerInfo;
        _logger     = logger;

        unsafe
        {
            CCSPlayerPawn_SetGlovesBodyGroup = (delegate* unmanaged<IntPtr, byte, void>) GetSetGlovesBodyGroupAddress();

            _logger.LogInformation("CCSPlayerPawn_SetGlovesBodyGroup 0x{addr:X}", (nint) CCSPlayerPawn_SetGlovesBodyGroup);
        }
    }

    public bool Init()
    {
        _bridge.HookManager.PlayerSpawnPost.InstallForward(OnPlayerSpawnPost);

        return true;
    }

    public void Shutdown()
    {
        _bridge.HookManager.PlayerSpawnPost.RemoveForward(OnPlayerSpawnPost);
    }

    private void OnPlayerSpawnPost(IPlayerSpawnForwardParams @params)
    {
        var client = @params.Client;

        if (client.IsFakeClient)
        {
            return;
        }

        var pawn = @params.Pawn;

        if (_playerInfo.GetPlayerGloves(client, pawn.Team) is not { } gloves
            || _playerInfo.GetPlayerWeaponSkin(client, (EconItemId) gloves) is not { } cosmetics)
        {
            return;
        }

        pawn.GiveGloves(gloves, cosmetics.PaintId, cosmetics.Wear, (int) cosmetics.Seed);

        unsafe
        {
            CCSPlayerPawn_SetGlovesBodyGroup(pawn.GetAbsPtr(), 0);
        }
    }

    private unsafe nint GetSetGlovesBodyGroupAddress()
    {
        var server = _bridge.ModuleManager.Server;

        var strAddr = server.FindStringExact("firstperson_default");

        if (strAddr == 0)
            return 0;

        var stringRefs = server.GetReferencesFromPointer(strAddr);

        if (stringRefs.Length == 0)
            return 0;

        foreach (var refInst in stringRefs)
        {
            // Get the function range containing the current reference (the initialization function itself)
            if (!server.GetFunctionRange(refInst, out var initFuncStart, out _))
                continue;

            // Decode the instruction at the reference, which should be `lea reg, [string]`
            // We only need to decode one instruction for analysis
            var codeReader = new PointerCodeReader((byte*) refInst);
            var decoder    = Decoder.Create(64, codeReader);
            decoder.IP = (ulong) refInst;

            var leaInstr = decoder.Decode();

            // Confirm this is a LEA instruction and get the target register
            if (leaInstr.Mnemonic != Mnemonic.Lea || leaInstr.Op0Kind != OpKind.Register)
                continue;

            var  targetReg  = leaInstr.Op0Register;
            nint pGlobalVar = 0;

            codeReader = new PointerCodeReader((byte*) refInst);
            decoder    = Decoder.Create(64, codeReader);
            decoder.IP = (ulong) refInst;

            var endAddress = (ulong) refInst + 0x50;

            while (decoder.IP < endAddress)
            {
                var instr = decoder.Decode();

                if (instr.IsInvalid || instr.Mnemonic == Mnemonic.Ret)
                    break;

                // Looking for the instruction logic: mov [RIP + Offset], targetReg
                if (instr.Mnemonic       == Mnemonic.Mov
                    && instr.Op0Kind     == OpKind.Memory
                    && instr.Op1Kind     == OpKind.Register
                    && instr.Op1Register == targetReg)
                {
                    pGlobalVar = (nint) instr.MemoryDisplacement64;

                    break;
                }
            }

            if (pGlobalVar == 0)
                continue;

            nint[] funcs;

            try
            {
                // Find all functions in the module that reference this global variable address
                funcs = server.FindFunctions(pGlobalVar);
            }
            catch
            {
                continue;
            }

            foreach (var func in funcs)
            {
                // Exclude the initialization function itself; the remaining match should be SetDefaultGloves
                if (func != initFuncStart)
                {
                    return func;
                }
            }
        }

        return 0;
    }

    private sealed unsafe class PointerCodeReader : CodeReader
    {
        private byte* _ptr;

        public PointerCodeReader(byte* ptr)
            => _ptr = ptr;

        public override int ReadByte()
            => *_ptr++;
    }
}
