using System.Linq;
namespace Minecraft;
public static class EquipmentTests
{
    public static void Run()
    {
        TestAssert.Check(ItemDb.WeaponOf(ItemId.WoodSword) is { Damage: 4.0f, Durability: 70 }, "wood sword stats");
        TestAssert.Check(ItemDb.WeaponOf(ItemId.StoneSword)!.Value.Damage > ItemDb.WeaponOf(ItemId.WoodSword)!.Value.Damage, "stone improves damage");
        TestAssert.Check(ItemDb.WeaponOf(ItemId.IronSword)!.Value.Durability > ItemDb.WeaponOf(ItemId.StoneSword)!.Value.Durability, "iron improves durability");
        TestAssert.Check(ItemDb.WeaponOf(ItemId.DiamondSword)!.Value.Damage > ItemDb.WeaponOf(ItemId.IronSword)!.Value.Damage, "diamond is best standard sword");
        TestAssert.Equal(1, ItemDb.MaxStack(ItemId.Shield), "shield is unstackable");
        TestAssert.Equal(384, ItemDb.New(ItemId.Bow, 1).Durability, "bow starts full");
        TestAssert.Check(!string.IsNullOrWhiteSpace(ItemDb.DescriptionOf(ItemId.DiamondSword)), "weapon description exists");
        Inventory inv = new(); inv.Add(ItemId.DiamondSword, 1); inv.DamageEquipment(0); TestAssert.Equal(899, inv.Get(0).Durability, "weapon wears on hit");
        Inventory loaded = new(); loaded.LoadFrom(inv.ToSave()); TestAssert.Equal(899, loaded.Get(0).Durability, "weapon durability persists");
        foreach(ItemId id in new[]{ItemId.WoodSword,ItemId.StoneSword,ItemId.IronSword,ItemId.DiamondSword,ItemId.Bow,ItemId.Arrow,ItemId.Shield,ItemId.EmberBlade})
            TestAssert.Check(Recipes.All.Any(r=>r.Result==id), $"recipe exists for {id}");
        Inventory mats=new();mats.Add(ItemId.Diamond,2);mats.Add(ItemId.Stick,1);Recipe ds=Recipes.All.First(r=>r.Result==ItemId.DiamondSword);
        TestAssert.Check(!Recipes.CanCraft(ds,mats), "diamond sword requires three diamonds");mats.Add(ItemId.Diamond,1);TestAssert.Check(Recipes.CanCraft(ds,mats), "diamond sword crafts with three diamonds");
    }
}