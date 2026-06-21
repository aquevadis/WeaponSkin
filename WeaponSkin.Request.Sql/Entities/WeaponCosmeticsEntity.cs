using SqlSugar;

namespace WeaponSkin.Request.Sql.Entities;

/// <summary>
///     Database entity representing weapon cosmetics configuration
/// </summary>
[SugarTable("ws_weapon_cosmetics")]
[SugarIndex($"unique_{{table}}_{nameof(SteamId)}_{nameof(ItemId)}", 
    nameof(SteamId), OrderByType.Asc,
    nameof(ItemId), OrderByType.Asc, IsUnique = true)]
public class WeaponCosmeticsEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(IsNullable = false)]
    public ulong SteamId { get; set; }

    [SugarColumn(IsNullable = false)]
    public int Team { get; set; }
    
    [SugarColumn(IsNullable = false)]
    public int ItemId { get; set; }

    [SugarColumn(IsNullable = false)]
    public ushort PaintId { get; set; }

    [SugarColumn(IsNullable = false, ColumnDataType = "float")]
    public float Wear { get; set; }

    [SugarColumn(IsNullable = false, ColumnDataType = "float")]
    public float Seed { get; set; }

    [SugarColumn(IsNullable = true)]
    public int? StatTrak { get; set; }

    [SugarColumn(IsNullable = true, Length = 255)]
    public string? NameTag { get; set; }

    /// <summary>
    ///     Sticker slot 0: "id;schema;offsetX;offsetY;wear;scale;rotation"
    ///     Default "0;0;0;0;0;0;0" means empty slot
    /// </summary>
    [SugarColumn(IsNullable = false, Length = 128)]
    public string WeaponSticker0 { get; set; } = "0;0;0;0;0;0;0";

    [SugarColumn(IsNullable = false, Length = 128)]
    public string WeaponSticker1 { get; set; } = "0;0;0;0;0;0;0";

    [SugarColumn(IsNullable = false, Length = 128)]
    public string WeaponSticker2 { get; set; } = "0;0;0;0;0;0;0";

    [SugarColumn(IsNullable = false, Length = 128)]
    public string WeaponSticker3 { get; set; } = "0;0;0;0;0;0;0";

    [SugarColumn(IsNullable = false, Length = 128)]
    public string WeaponSticker4 { get; set; } = "0;0;0;0;0;0;0";

    /// <summary>
    ///     Keychain: "id;x;y;z;seed"
    ///     Default "0;0;0;0;0" means no keychain
    /// </summary>
    [SugarColumn(IsNullable = false, Length = 128)]
    public string WeaponKeychain { get; set; } = "0;0;0;0;0";
}
