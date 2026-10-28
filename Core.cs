using Alta;
using Alta.Chunks;
using Alta.Console.Commands;
using Alta.Inventory;
using Alta.Liquid;
using Alta.Networking;
using Alta.Networking.Servers;
using CookingLib;
using HarmonyLib;
using MelonLoader;
using System.Reflection;
using UnityEngine;

[assembly: MelonInfo(typeof(Ambrosia.Core), "Ambrosia", "1.0.0", "CGNik", null)]
[assembly: MelonGame("Alta", "A Township Tale")]

namespace Ambrosia
{
    using Alta.Pages;
    using DyeingAPI;
    internal class DyeingAPICrossmod
    {
        internal static void AddAmbrosiaToGlobalLiquidBlacklist()
        {
            DyeingAPI.DyeRecipe.globalLiquidBlacklist.Add(43670u);
        }
    }

    public class OnTriggerEnterPatch
    {
        internal static void Postfix(LiquidContainerTrigger __instance, Collider other)
        {

            if (__instance.GetComponentInParent<LiquidContainer>()?.ContainedLiquid?.Liquid?.Hash != 43670u) { return; }

            NetworkPrefab? networkPrefab_item = other.GetComponentInParent<NetworkPrefab>();

            if (networkPrefab_item == null) { return; }

            if (Core.ambrosiaRecipeImmuneIdentifiers.Contains(networkPrefab_item.Entity.Identifier)) { return; }

            if (Core.ambrosiaRecipeCurrentlyBeingInputIdentifiers.Contains(networkPrefab_item.Entity.Identifier)) { return; }

            uint prefabHash = networkPrefab_item.Hash;
            int prefabCount = 1;

            if (networkPrefab_item.Hash == 38942u)
            {
                DockArray dockArray = networkPrefab_item.gameObject.GetComponent<DockArray>();
                PickupDock pickupDock = ((List<PickupDock>)typeof(DockArray).GetField("docks", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dockArray))[0];
                prefabHash = pickupDock.dockedType.Prefab.Hash;
                prefabCount = pickupDock.QuantityStored;
            }

            AmbrosiaRecipe? ambrosiaRecipe = AmbrosiaRecipe.GetValidAmbrosiaRecipe(networkPrefab_item, prefabHash, prefabCount);

            if (ambrosiaRecipe == null) { return; }

            Core.ambrosiaRecipeCurrentlyBeingInputIdentifiers.Add(networkPrefab_item.Entity.Identifier);

            List<HashCount> outputs = ambrosiaRecipe.outputs
                .Select(output => new HashCount(output.hash, output.count))
                .ToList();
            if (ambrosiaRecipe.modifyOutputs != null)
            {
                try
                {
                    outputs = ambrosiaRecipe.modifyOutputs.Invoke(networkPrefab_item, outputs);
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"Error in modifyOutputs for AmbrosiaRecipe with Hash {ambrosiaRecipe.Hash}: {ex}");
                }
            }

            List<NetworkEntity> networkEntities_outputItems = [];
            if (outputs.Count == 1)
            {
                Item? itemToSpawn = Item.All.Where(item => item.Prefab?.Hash == outputs[0].hash).FirstOrDefault();
                if (itemToSpawn != null)
                {
                    NetworkEntity networkEntity_outputItem = SpawnHelper.SpawnPouchOrPickup(itemToSpawn, outputs[0].count, SpawnData.DontGenerate, networkPrefab_item.Entity.Chunk, networkPrefab_item.transform.position, networkPrefab_item.transform.rotation);
                    Core.ambrosiaRecipeImmuneIdentifiers.Add(networkEntity_outputItem.Identifier);
                    networkEntities_outputItems.Add(networkEntity_outputItem);
                    networkPrefab_item.gameObject.GetComponent<Pickup>().ReplaceWith(networkEntity_outputItem.CommonPickup);
                }
            }
            else
            {
                foreach (HashCount output in outputs)
                {
                    Item? itemToSpawn = Item.All.Where(item => item.Prefab?.Hash == output.hash).FirstOrDefault();
                    if (itemToSpawn != null)
                    {
                        NetworkEntity networkEntity_outputItem = SpawnHelper.SpawnPouchOrPickup(itemToSpawn, output.count, SpawnData.DontGenerate, networkPrefab_item.Entity.Chunk, networkPrefab_item.transform.position, networkPrefab_item.transform.rotation);
                        Core.ambrosiaRecipeImmuneIdentifiers.Add(networkEntity_outputItem.Identifier);
                        networkEntities_outputItems.Add(networkEntity_outputItem);
                    }
                }
            }

            try
            {
                ambrosiaRecipe.postTransmutation?.Invoke(networkPrefab_item, networkEntities_outputItems);
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"Error in postTransmutation for AmbrosiaRecipe with Hash {ambrosiaRecipe.Hash}: {ex}");
            }

            uint identifierToRemove = networkPrefab_item.Entity.Identifier;
            networkPrefab_item.Entity.SceneDestroy();
        }
    }

    public class OnTriggerExitPatch
    {
        internal static void Postfix(LiquidContainerTrigger __instance, Collider other)
        {
            if (__instance.GetComponentInParent<LiquidContainer>()?.ContainedLiquid?.Liquid?.Hash != 43670u) { return; }

            NetworkPrefab? networkPrefab_item = other.GetComponentInParent<NetworkPrefab>();

            if (networkPrefab_item == null) { return; }

            if (Core.ambrosiaRecipeImmuneIdentifiers.Contains(networkPrefab_item.Entity.Identifier))
            {
                Core.ambrosiaRecipeImmuneIdentifiers.Remove(networkPrefab_item.Entity.Identifier);
            }
        }
    }

    public class AmbrosiaRecipe : HashedGeneralValue<AmbrosiaRecipe>
    {
        /// <summary>
        /// Initializes an AmbrosiaRecipe by setting its values and adding it to AmbrosiaRecipe.All.
        /// </summary>
        public void Initialize(string name, int hash, List<HashCount> inputs, List<HashCount> outputs, Func<NetworkPrefab, bool> inputCondition, Func<NetworkPrefab, List<HashCount>, List<HashCount>> modifyOutputs, Action<NetworkPrefab, List<NetworkEntity>> postTransmutation)
        {
            this.name = name;
            this.hash = hash;
            this.inputs = inputs;
            this.outputs = outputs;
            this.inputCondition = inputCondition;
            this.modifyOutputs = modifyOutputs;
            this.postTransmutation = postTransmutation;

            this.Register();
        }

        /// <summary>
        /// Creates, initializes, and return an AmbrosiaRecipe.
        /// </summary>
        /// <param name="name">The name of the UnityObject of the AmbrosiaRecipe.</param>
        /// <param name="hash">The hash of the AmbrosiaRecipe.</param>
        /// <param name="inputs">List of HashCounts (Hash of a prefab, count of how many) that can serve as inputs. ANY of these inputs work for the recipe, you only need ONE of the HashCounts for the recipe to go through. Counts above 1 require the item to be in a Pouch to work.</param>
        /// <param name="outputs">List of HashCounts (Hash of a prefab, count of how many) that will be output. ALL of these will be spawned. Counts above 1 will output as Pouches.</param>
        /// <param name="inputCondition">A Func that returns a bool determining whether or not the recipe is valid (true means that it's valid), allowing you to filter inputs with more detail. Takes a NetworkPrefab (the input prefab). To easily handle Pouches, Core.GetSlotInDockArray() can be used.</param>
        /// <param name="modifyOutputs">A Func that returns a List<HashCount> that will be used as the outputs for the recipe, allowing you to add more logic to how your recipes work. Takes a NetworkPrefab (the input prefab) and a List<HashCount> (the original outputs).</param>
        /// <param name="postTransmutation">An Action that runs after a recipe has been done, allowing you to do things to the outputted prefabs. Takes a NetworkPrefab (the input prefab) and a List<NetworkEntity> (the list of outputted items).</param>
        /// <returns>Returns the AmbrosiaRecipe that was created.</returns>
        public static AmbrosiaRecipe Create(string name, int hash, List<HashCount> inputs, List<HashCount> outputs, Func<NetworkPrefab, bool> inputCondition = null, Func<NetworkPrefab, List<HashCount>, List<HashCount>> modifyOutputs = null, Action<NetworkPrefab, List<NetworkEntity>> postTransmutation = null)
        {
            AmbrosiaRecipe ambrosiaRecipe = ScriptableObject.CreateInstance<AmbrosiaRecipe>();
            ambrosiaRecipe.Initialize(name, hash, inputs, outputs, inputCondition, modifyOutputs, postTransmutation);
            return ambrosiaRecipe;
        }

        public List<HashCount> inputs;
        public List<HashCount> outputs;
        public Func<NetworkPrefab, bool> inputCondition;
        public Func<NetworkPrefab, List<HashCount>, List<HashCount>> modifyOutputs;
        public Action<NetworkPrefab, List<NetworkEntity>> postTransmutation;

        public static AmbrosiaRecipe GetValidAmbrosiaRecipe(NetworkPrefab inputPrefab, uint hash, int count)
        {
            foreach (AmbrosiaRecipe ambrosiaRecipe in AmbrosiaRecipe.All)
            {
                foreach (HashCount input in ambrosiaRecipe.inputs)
                {
                    if ((input.hash == hash || input.acceptAnyHash) && (count == input.count || input.acceptAnyCount))
                    {
                        if (ambrosiaRecipe.inputCondition?.Invoke(inputPrefab) ?? true)
                        {
                            return ambrosiaRecipe;
                        }
                    }
                }
            }

            return null;
        }

        public void Register()
        {
            AmbrosiaRecipe.CheckItems();
            Dictionary<uint, AmbrosiaRecipe> items = (Dictionary<uint, AmbrosiaRecipe>)typeof(HashedGeneralValue<AmbrosiaRecipe>).GetField("items", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            items.Add(this.Hash, this);
        }
    }

    public class HashCount
    {
        public HashCount(uint hash, int count, bool acceptAnyHash = false, bool acceptAnyCount = false)
        {
            this.hash = hash;
            this.count = count;
            this.acceptAnyHash = acceptAnyHash;
            this.acceptAnyCount = acceptAnyCount;
        }

        public uint hash;
        public int count;
        public bool acceptAnyHash = false;
        public bool acceptAnyCount = false;
    }

    public class Core : MelonMod
    {
        private static MelonPreferences_Category melonPrefCategory;
        private static MelonPreferences_Entry<bool> melonPrefEntry_ShouldSpawnInfiniteCrateOnNextBootup;

        public static event Action PreSetUpAmbrosiaRecipes = () => { };
        public static event Action SetUpAmbrosiaRecipes = () => { };
        public static event Action PostSetUpAmbrosiaRecipes = () => { };

        public static List<uint> ambrosiaRecipeImmuneIdentifiers = [];
        public static List<uint> ambrosiaRecipeCurrentlyBeingInputIdentifiers = [];

        internal static LibLiquid ambrosia;

        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("Initialized.");

            melonPrefCategory = MelonPreferences.CreateCategory("Ambrosia");
            melonPrefEntry_ShouldSpawnInfiniteCrateOnNextBootup = melonPrefCategory.CreateEntry<bool>("shouldSpawnInfiniteCrateOnNextBootup", true);
            MelonPreferences.Save();

            ServerHandler.Initialized += ServerInitialized;
            CookingLib.Main.Setup += SetupLiquids;
            SetUpAmbrosiaRecipes += SetUpBaseAmbrosiaRecipes;
        }

        private void ServerInitialized(ServerHandler serverHandler)
        {
            HarmonyInstance.Patch(AccessTools.Method(typeof(LiquidContainerTrigger), "OnTriggerEnter"), postfix: new HarmonyMethod(typeof(OnTriggerEnterPatch), nameof(OnTriggerEnterPatch.Postfix)));
            HarmonyInstance.Patch(AccessTools.Method(typeof(LiquidContainerTrigger), "OnTriggerExit"), postfix: new HarmonyMethod(typeof(OnTriggerExitPatch), nameof(OnTriggerExitPatch.Postfix)));

            serverHandler.BootupFinished += BootupFinished;
        }

        private void BootupFinished()
        {
            if (melonPrefEntry_ShouldSpawnInfiniteCrateOnNextBootup.Value)
            {
                NetworkEntity networkEntity_concoctionCrate = SpawnHelper.Spawn(23304u, SpawnData.DontGenerate, LocationChunkHelper.GetBestContaining(new Vector3(-1340.8f, 299.5f, 15.9f)), new Vector3(-1340.8f, 299.5f, 15.9f),
                    preChunking: (NetworkEntity networkEntity) =>
                    {
                        LiquidContainer liquidContainer = networkEntity.gameObject.GetComponent<LiquidContainer>();
                        LiquidContent liquidContent = ambrosia.Definition.GetAsLiquidContent();
                        ((ILiquidContainerInternal)liquidContainer).SetContent(liquidContent);
                    }
                );

                melonPrefEntry_ShouldSpawnInfiniteCrateOnNextBootup.Value = false;
                MelonPreferences.Save();
                typeof(SavingCommandModule).GetMethod("Save", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            }
        }

        public override void OnLateInitializeMelon()
        {
            PreSetUpAmbrosiaRecipes.Invoke();

            SetUpAmbrosiaRecipes.Invoke();

            PostSetUpAmbrosiaRecipes.Invoke();
        }

        private static void SetupLiquids()
        {
            ambrosia = Liquids.RegisterShared(
                43670,
                new LiquidSpec
                {
                    Id = "Ambrosia.Ambrosia",
                    Name = "Ambrosia",
                    Look = "Teleport Liquid Visual Data",
                    Color = new Color(0.7f, 0.3f, 0f),
                    Effects = {
                        new EffectSpec { Effect = "Heal", Strength = 0.5f, OverSeconds = 0 },
                        new EffectSpec { Effect = "Heal", Strength = 0.5f, OverSeconds = 10 },
                        new EffectSpec { Effect = "Heal", Strength = 0.5f, OverSeconds = 20 },
                        new EffectSpec { Effect = "Heal", Strength = 0.5f, OverSeconds = 30 }
                    }
                }
            );

            Infusions.Register(
                new InfusionSpec
                {
                    Id = "Ambrosia.AmbrosiaInfusion",
                    InputLiquids = ["Water"],
                    Item = "MRKFuelCore",
                    OutputLiquid = "Ambrosia.Ambrosia",
                    ConsumeItem = true
                }
            );

            if (MelonBase.FindMelon("DyeingAPI", "CGNik") != null)
            {
                DyeingAPICrossmod.AddAmbrosiaToGlobalLiquidBlacklist();
            }
        }

        private void SetUpBaseAmbrosiaRecipes()
        {
            AmbrosiaRecipe.Create(
                "Rope to Canvas Recipe",
                1,
                [
                    new HashCount(43836u, 1)
                ],
                [
                    new HashCount(34570u, 1)
                ]
            );

            AmbrosiaRecipe.Create(
                "Canvas to Rope Recipe",
                2,
                [
                    new HashCount(34570u, 1)
                ],
                [
                    new HashCount(43836u, 1)
                ]
            );

            AmbrosiaRecipe.Create(
                "Redwood Wedges to Redwood Gotera Core Recipe",
                3,
                [
                    new HashCount(47118u, 50)
                ],
                [
                    new HashCount(41402u, 1)
                ],
                inputCondition: (NetworkPrefab inputPrefab) =>
                {
                    if (GetSlotInDockArray(inputPrefab.gameObject.GetComponent<DockArray>(), 0).DockedPickup.gameObject.GetComponent<PhysicalMaterialPart>().PhysicalMaterial.Hash == 10962u) { return true; }
                    return false;
                }
            );

            AmbrosiaRecipe.Create(
                "Blue Feather to Red Feather Recipe",
                4,
                [
                    new HashCount(7918u, 1)
                ],
                [
                    new HashCount(18734u, 1)
                ],
                modifyOutputs: (NetworkPrefab inputPrefab, List<HashCount> outputs) =>
                {
                    System.Random rng = new System.Random();
                    if (rng.NextFloat() <= 0.5f) { return []; }
                    return outputs;
                }
            );

            AmbrosiaRecipe.Create(
                "Red Feather to Blue Feather Recipe",
                5,
                [
                    new HashCount(18734u, 1)
                ],
                [
                    new HashCount(7918u, 1)
                ]
            );

            AmbrosiaRecipe.Create(
                "Rusty Tool to Iron Ingot Recipe",
                6,
                [
                    new HashCount(4412u, 1),
                    new HashCount(40366u, 1),
                    new HashCount(29296u, 1),
                    new HashCount(63156u, 1),
                    new HashCount(36126u, 1),
                    new HashCount(46874u, 1),
                    new HashCount(59586u, 1),
                    new HashCount(20684u, 1),
                    new HashCount(44824u, 1),
                    new HashCount(33230u, 1)
                ],
                [
                    new HashCount(23012u, 1)
                ]
            );

            AmbrosiaRecipe.Create(
                "Gourd to Potions Recipe",
                7,
                [
                    new HashCount(29032u, 1)
                ],
                [
                    new HashCount(23644u, 1),
                    new HashCount(23644u, 1)
                ],
                inputCondition: (NetworkPrefab inputPrefab) =>
                {
                    if (inputPrefab.gameObject.GetComponent<LiquidContainer>().ContainedLiquid != null) { return false; }
                    return true;
                },
                postTransmutation: (NetworkPrefab inputPrefab, List<NetworkEntity> outputs) =>
                {
                    foreach (NetworkEntity networkEntity in outputs)
                    {
                        networkEntity.gameObject.GetComponent<LiquidContainer>().ForceEmpty();
                    }
                }
            );

            AmbrosiaRecipe.Create(
                "Standard Crafted Mould Decraft Recipe",
                8,
                [
                    new HashCount(38578u, 1)
                ],
                [
                    new HashCount(38578u, 1),
                    new HashCount(0u, 1)
                ],
                inputCondition: (NetworkPrefab inputPrefab) =>
                {
                    if (inputPrefab.gameObject.GetComponent<Mould>().Definition != null) { return true; }
                    return false;
                },
                modifyOutputs: (NetworkPrefab inputPrefab, List<HashCount> outputs) =>
                {
                    outputs[1].hash = inputPrefab.gameObject.GetComponent<Mould>().Definition.Product.Prefab.Hash;
                    return outputs;
                },
                postTransmutation: (NetworkPrefab inputPrefab, List<NetworkEntity> outputs) =>
                {
                    typeof(Mould).GetField("usage", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(outputs[0].gameObject.GetComponent<Mould>(), inputPrefab.gameObject.GetComponent<Mould>().Usage);
                    outputs[1].gameObject.GetComponent<PhysicalMaterialPart>().SetMaterial(PhysicalMaterial.All.Where(mat => mat.Hash == 1u).First());
                }
            );

            AmbrosiaRecipe.Create(
                "Woodcut Wedge Restore Recipe",
                9,
                [
                    new HashCount(290u, 1),
                    new HashCount(58068u, 1),
                    new HashCount(232u, 1)
                ],
                [
                    new HashCount(47118u, 1)
                ],
                postTransmutation: (NetworkPrefab inputPrefab, List<NetworkEntity> outputs) =>
                {
                    outputs[0].gameObject.GetComponent<PhysicalMaterialPart>().SetMaterial(inputPrefab.gameObject.GetComponent<PhysicalMaterialPart>().PhysicalMaterial);
                }
            );

            AmbrosiaRecipe.Create(
                "Precrafted Hebios Weapon to Coal Recipe",
                10,
                [
                    new HashCount(34994u, 1),
                    new HashCount(41052u, 1),
                    new HashCount(27038u, 1),
                    new HashCount(29856u, 1)
                ],
                [
                    new HashCount(19658u, 1)
                ]
            );

            AmbrosiaRecipe.Create(
                "Katana Blade to Katana Handle Recipe",
                11,
                [
                    new HashCount(17266u, 1)
                ],
                [
                    new HashCount(53200u, 1)
                ]
            );

            AmbrosiaRecipe.Create(
                "Katana Handle to Katana Blade Recipe",
                12,
                [
                    new HashCount(53200u, 1)
                ],
                [
                    new HashCount(17266u, 1)
                ]
            );

            AmbrosiaRecipe.Create(
                "Naginata Blade to Naginata Handle Recipe",
                13,
                [
                    new HashCount(17420u, 1)
                ],
                [
                    new HashCount(24220u, 1)
                ]
            );

            AmbrosiaRecipe.Create(
                "Naginata Handle to Naginata Blade Recipe",
                14,
                [
                    new HashCount(24220u, 1)
                ],
                [
                    new HashCount(17420u, 1)
                ]
            );

            AmbrosiaRecipe.Create(
                "Wakizashi Blade to Wakizashi Handle Recipe",
                15,
                [
                    new HashCount(9018u, 1)
                ],
                [
                    new HashCount(47662u, 1)
                ]
            );

            AmbrosiaRecipe.Create(
                "Wakizashi Handle to Wakizashi Blade Recipe",
                16,
                [
                    new HashCount(47662u, 1)
                ],
                [
                    new HashCount(9018u, 1)
                ]
            );

            AmbrosiaRecipe.Create(
                "Copper Ingot to Copper Ore Recipe",
                17,
                [
                    new HashCount(32850u, 1)
                ],
                [
                    new HashCount(2484u, 3)
                ]
            );

            AmbrosiaRecipe.Create(
                "Iron Ingot to Iron Ore Recipe",
                18,
                [
                    new HashCount(23012u, 1)
                ],
                [
                    new HashCount(61652u, 3)
                ]
            );

            AmbrosiaRecipe.Create(
                "Gold Ingot to Gold Ore Recipe",
                19,
                [
                    new HashCount(30092u, 1)
                ],
                [
                    new HashCount(61650u, 3)
                ]
            );

            AmbrosiaRecipe.Create(
                "Silver Ingot to Silver Ore Recipe",
                20,
                [
                    new HashCount(23528u, 1)
                ],
                [
                    new HashCount(9586u, 3)
                ]
            );

            AmbrosiaRecipe.Create(
                "Mythril Ingot to Mythril Ore Recipe",
                21,
                [
                    new HashCount(24774u, 1)
                ],
                [
                    new HashCount(11702u, 3)
                ]
            );

            AmbrosiaRecipe.Create(
                "Leather Rolls to Big Leather Recipe",
                22,
                [
                    new HashCount(47760u, 15),
                ],
                [
                    new HashCount(23206u, 1)
                ],
                postTransmutation: (NetworkPrefab inputPrefab, List<NetworkEntity> outputs) =>
                {
                    outputs[0].gameObject.GetComponent<PhysicalMaterialPart>().SetMaterial(GetSlotInDockArray(inputPrefab.gameObject.GetComponent<DockArray>(), 0).DockedPickup.gameObject.GetComponent<PhysicalMaterialPart>().PhysicalMaterial);
                }
            );

            AmbrosiaRecipe.Create(
                "Leather Strips to Leather Roll Recipe",
                23,
                [
                    new HashCount(63204u, 3),
                ],
                [
                    new HashCount(47760u, 1)
                ],
                postTransmutation: (NetworkPrefab inputPrefab, List<NetworkEntity> outputs) =>
                {
                    outputs[0].gameObject.GetComponent<PhysicalMaterialPart>().SetMaterial(GetSlotInDockArray(inputPrefab.gameObject.GetComponent<DockArray>(), 0).DockedPickup.gameObject.GetComponent<PhysicalMaterialPart>().PhysicalMaterial);
                }
            );

            AmbrosiaRecipe.Create(
                "Lantern to Flashlight Lantern Recipe",
                24,
                [
                    new HashCount(41760u, 1)
                ],
                [
                    new HashCount(23642u, 1)
                ],
                inputCondition: (NetworkPrefab inputPrefab) =>
                {
                    if (GetSlotInDockArray(inputPrefab.gameObject.GetComponent<DockArray>(), 0).HasDockedPickup) { return false; }
                    return true;
                }
            );

            AmbrosiaRecipe.Create(
                "Flashlight Lantern to Lantern Recipe",
                25,
                [
                    new HashCount(23642u, 1)
                ],
                [
                    new HashCount(41760u, 1)
                ],
                inputCondition: (NetworkPrefab inputPrefab) =>
                {
                    if (GetSlotInDockArray(inputPrefab.gameObject.GetComponent<DockArray>(), 0).HasDockedPickup) { return false; }
                    return true;
                }
            );

            AmbrosiaRecipe.Create(
                "Iron Ore to Copper Ore Recipe",
                26,
                [
                    new HashCount(61652u, 1)
                ],
                [
                    new HashCount(2484u, 1)
                ]
            );

            AmbrosiaRecipe.Create(
                "Gold Ore to Iron Ore Recipe",
                27,
                [
                    new HashCount(61650u, 1)
                ],
                [
                    new HashCount(61652u, 1)
                ]
            );

            AmbrosiaRecipe.Create(
                "Silver Ore to Gold Ore Recipe",
                28,
                [
                    new HashCount(9586u, 1)
                ],
                [
                    new HashCount(61650u, 1)
                ]
            );

            AmbrosiaRecipe.Create(
                "Mythril Ore to Silver Ore Recipe",
                29,
                [
                    new HashCount(11702u, 1)
                ],
                [
                    new HashCount(9586u, 1)
                ]
            );

            AmbrosiaRecipe.Create(
                "Carsi Ingot Decraft Recipe",
                30,
                [
                    new HashCount(60868u, 1)
                ],
                [
                    new HashCount(9586u, 4),
                    new HashCount(31034u, 1)
                ]
            );

            AmbrosiaRecipe.Create(
                "Evinon Steel Ingot Decraft Recipe",
                31,
                [
                    new HashCount(32198u, 1)
                ],
                [
                    new HashCount(24774u, 2),
                    new HashCount(23528u, 1)
                ]
            );

            AmbrosiaRecipe.Create(
                "Orchi Ingot Decraft Recipe",
                32,
                [
                    new HashCount(16464u, 1)
                ],
                [
                    new HashCount(61650u, 3),
                    new HashCount(11702u, 2)
                ]
            );

            AmbrosiaRecipe.Create(
                "Red Iron Ingot Decraft Recipe",
                33,
                [
                    new HashCount(31034u, 1)
                ],
                [
                    new HashCount(23012u, 1),
                    new HashCount(32850u, 1)
                ]
            );

            AmbrosiaRecipe.Create(
                "White Gold Ingot Decraft Recipe",
                34,
                [
                    new HashCount(13158u, 1)
                ],
                [
                    new HashCount(9586u, 3),
                    new HashCount(61650u, 2)
                ]
            );

            AmbrosiaRecipe.Create(
                "Fireworks to Explosive Shard Recipe",
                35,
                [
                    new HashCount(40748u, 2)
                ],
                [
                    new HashCount(50412u, 1)
                ]
            );

            AmbrosiaRecipe.Create(
                "Explosive Shards to Dynamite Recipe",
                36,
                [
                    new HashCount(50412u, 2)
                ],
                [
                    new HashCount(31326u, 1)
                ]
            );

            AmbrosiaRecipe.Create(
                "Dynamite to Fireworks Recipe",
                37,
                [
                    new HashCount(31326u, 1)
                ],
                [
                    new HashCount(40748u, 4)
                ]
            );

            AmbrosiaRecipe.Create(
                "Firework to Firework Recipe",
                38,
                [
                    new HashCount(40748u, 1)
                ],
                [
                    new HashCount(40748u, 1)
                ]
            );
        }

        public static PickupDock GetSlotInDockArray(DockArray dockArray, int index)
        {
            return ((List<PickupDock>)typeof(DockArray).GetField("docks", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dockArray))[index];
        }
    }
}