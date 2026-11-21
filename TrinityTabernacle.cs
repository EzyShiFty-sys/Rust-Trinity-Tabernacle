using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Rust;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Trinity Tabernacle", "6ix06ix", "1.0.0")]
    [Description("Automatically spawns an indestructible, no-decay church (Trinity Tabernacle) on flat terrain every wipe.")]
    public class TrinityTabernacle : RustPlugin
    {
        #region Fields & Constants

        private const string PermAdmin = "trinitytabernacle.admin";

        private const string PrefabFoundation = "assets/prefabs/building core/foundation/foundation.prefab";
        private const string PrefabFoundationSteps = "assets/prefabs/building core/foundation.steps/foundation.steps.prefab";
        private const string PrefabWall = "assets/prefabs/building core/wall/wall.prefab";
        private const string PrefabWallDoorway = "assets/prefabs/building core/wall.doorway/wall.doorway.prefab";
        private const string PrefabWallWindow = "assets/prefabs/building core/wall.window/wall.window.prefab";
        private const string PrefabWallHalf = "assets/prefabs/building core/wall.half/wall.half.prefab";
        private const string PrefabFloor = "assets/prefabs/building core/floor/floor.prefab";
        private const string PrefabRoof = "assets/prefabs/building core/roof/roof.prefab";
        private const string PrefabDoorWood = "assets/prefabs/building/door.hinged/door.hinged.wood.prefab";

        private const string PrefabChair = "assets/prefabs/deployable/chair/chair.deployed.prefab";
        private const string PrefabTable = "assets/prefabs/deployable/table/table.deployed.prefab";
        private const string PrefabLantern = "assets/prefabs/deployable/lantern/lantern.deployed.prefab";
        private const string PrefabFireplace = "assets/prefabs/deployable/fireplace/fireplace.deployed.prefab";
        private const string PrefabRug = "assets/prefabs/deployable/rug/rug.deployed.prefab";

        private const float BlockSize = 3.0f;
        private const float WallHeight = 3.0f;

        private PluginConfig _config;
        private PluginData _data;
        private bool _pendingWipeSpawn;

        #endregion

        #region Configuration

        private class PluginConfig
        {
            [JsonProperty("Church Name")]
            public string ChurchName { get; set; } = "Trinity Tabernacle";

            [JsonProperty("Auto Spawn On Wipe")]
            public bool AutoSpawnOnWipe { get; set; } = true;

            [JsonProperty("Building Grade (Twigs, Wood, Stone, Metal, TopTier)")]
            public string BuildingGrade { get; set; } = "Stone";

            [JsonProperty("Indestructible & No Decay")]
            public bool Indestructible { get; set; } = true;

            [JsonProperty("Prevent Player Building Radius (meters, 0 to disable)")]
            public float PreventBuildingRadius { get; set; } = 25f;

            [JsonProperty("Keep Altar Lights & Hearth Lit")]
            public bool KeepLightsLit { get; set; } = true;

            [JsonProperty("Max Terrain Slope (meters)")]
            public float MaxTerrainSlope { get; set; } = 1.4f;

            [JsonProperty("Min Distance From Monuments (meters)")]
            public float MinMonumentDistance { get; set; } = 130f;

            [JsonProperty("Map Search Radius Percent (0.1 - 0.85)")]
            public float SearchRadiusPercent { get; set; } = 0.65f;

            [JsonProperty("Allow /church Command For Players")]
            public bool AllowPlayerCommand { get; set; } = true;
        }

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null)
                {
                    LoadDefaultConfig();
                }
            }
            catch
            {
                PrintWarning("Configuration file is corrupt. Loading default configuration.");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        #endregion

        #region Data Persistence

        private class PluginData
        {
            public bool HasSpawnedThisWipe { get; set; }
            public Vector3 Origin { get; set; }
            public float RotationY { get; set; }
            public HashSet<ulong> EntityIds { get; set; } = new HashSet<ulong>();
        }

        private void LoadData()
        {
            try
            {
                _data = Interface.Oxide.DataFileSystem.ReadObject<PluginData>(Name) ?? new PluginData();
                if (_data.EntityIds == null)
                {
                    _data.EntityIds = new HashSet<ulong>();
                }
            }
            catch
            {
                _data = new PluginData();
            }
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name, _data);
        }

        #endregion

        #region Oxide Hooks

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            LoadData();
        }

        private void OnNewSave(string filename)
        {
            _pendingWipeSpawn = true;
            _data = new PluginData();
            SaveData();
            Puts("New wipe detected. Trinity Tabernacle will spawn automatically once the server finishes initializing.");
        }

        private void OnServerInitialized()
        {
            if (_config.AutoSpawnOnWipe && (_pendingWipeSpawn || !_data.HasSpawnedThisWipe))
            {
                _pendingWipeSpawn = false;
                timer.Once(3f, () =>
                {
                    if (_data.HasSpawnedThisWipe && _data.EntityIds.Count > 0)
                    {
                        return;
                    }

                    SpawnAtBestFlatTerrain();
                });
            }
            else if (_data.HasSpawnedThisWipe && _config.KeepLightsLit)
            {
                RefreshLitEntities();
            }
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (!_config.Indestructible || entity == null || entity.net == null)
            {
                return null;
            }

            if (_data.EntityIds.Contains(entity.net.ID.Value))
            {
                return true;
            }

            return null;
        }

        private object OnStructureUpgrade(BuildingBlock block, BasePlayer player, BuildingGrade.Enum grade)
        {
            if (!_config.Indestructible || block == null || block.net == null)
            {
                return null;
            }

            if (_data.EntityIds.Contains(block.net.ID.Value))
            {
                return true;
            }

            return null;
        }

        private object OnStructureDemolish(BuildingBlock block, BasePlayer player, bool immediate)
        {
            if (!_config.Indestructible || block == null || block.net == null)
            {
                return null;
            }

            if (_data.EntityIds.Contains(block.net.ID.Value))
            {
                return true;
            }

            return null;
        }

        private object CanPickupEntity(BasePlayer player, BaseEntity entity)
        {
            if (!_config.Indestructible || entity == null || entity.net == null)
            {
                return null;
            }

            if (_data.EntityIds.Contains(entity.net.ID.Value))
            {
                return false;
            }

            return null;
        }

        private object CanLootEntity(BasePlayer player, StorageContainer container)
        {
            if (container == null || container.net == null)
            {
                return null;
            }

            if (_data.EntityIds.Contains(container.net.ID.Value))
            {
                return false;
            }

            return null;
        }

        private object OnFuelConsume(BaseOven oven, Item fuel, ItemModBurnable burnable)
        {
            if (!_config.KeepLightsLit || oven == null || oven.net == null)
            {
                return null;
            }

            if (_data.EntityIds.Contains(oven.net.ID.Value))
            {
                return true;
            }

            return null;
        }

        private object CanBuild(Planner planner, Construction prefab, Construction.Target target)
        {
            if (_config.PreventBuildingRadius <= 0f || !_data.HasSpawnedThisWipe)
            {
                return null;
            }

            var player = planner?.GetOwnerPlayer();
            if (player != null && permission.UserHasPermission(player.UserIDString, PermAdmin))
            {
                return null;
            }

            var churchCenter = _data.Origin + Quaternion.Euler(0f, _data.RotationY, 0f) * new Vector3(0f, 0f, BlockSize * 3f);
            if (Vector3.Distance(target.position, churchCenter) <= _config.PreventBuildingRadius)
            {
                if (player != null)
                {
                    SendReply(player, $"You cannot build near {_config.ChurchName}.");
                }

                return false;
            }

            return null;
        }

        #endregion

        #region Commands

        [ChatCommand("church")]
        private void CmdChurch(BasePlayer player, string command, string[] args)
        {
            if (!_config.AllowPlayerCommand && !HasAdminPermission(player))
            {
                return;
            }

            if (!_data.HasSpawnedThisWipe || _data.EntityIds.Count == 0)
            {
                SendReply(player, $"{_config.ChurchName} has not been placed yet this wipe.");
                return;
            }

            var grid = GetGridCoord(_data.Origin);
            SendReply(player, $"{_config.ChurchName} is located at grid <color=#FFD700>{grid}</color> ({_data.Origin.x:F0}, {_data.Origin.z:F0}). All are welcome!");
        }

        [ChatCommand("spawntabernacle")]
        private void CmdSpawnTabernacle(BasePlayer player, string command, string[] args)
        {
            if (!HasAdminPermission(player))
            {
                SendReply(player, "You do not have permission to use this command.");
                return;
            }

            if (args.Length > 0 && args[0].Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                if (SpawnAtBestFlatTerrain())
                {
                    SendReply(player, $"{_config.ChurchName} spawned at flat terrain (grid {GetGridCoord(_data.Origin)}).");
                }
                else
                {
                    SendReply(player, "Could not find suitable flat terrain. Try standing where you want it and running /spawntabernacle.");
                }
                return;
            }

            var origin = player.transform.position;
            origin.y = TerrainMeta.HeightMap.GetHeight(origin) + 0.45f;
            var yaw = Mathf.Round(player.eyes.rotation.eulerAngles.y / 90f) * 90f;

            RemoveExistingChurch();
            SpawnChurch(origin, yaw);
            SendReply(player, $"{_config.ChurchName} spawned at your location (grid {GetGridCoord(origin)}).");
        }

        [ChatCommand("removetabernacle")]
        private void CmdRemoveTabernacle(BasePlayer player, string command, string[] args)
        {
            if (!HasAdminPermission(player))
            {
                SendReply(player, "You do not have permission to use this command.");
                return;
            }

            int removed = RemoveExistingChurch();
            SendReply(player, $"Removed {removed} entities from {_config.ChurchName}.");
        }

        [ConsoleCommand("tabernacle.spawn")]
        private void ConsoleSpawn(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin)
            {
                return;
            }

            if (SpawnAtBestFlatTerrain())
            {
                arg.ReplyWith($"{_config.ChurchName} spawned at {_data.Origin} (grid {GetGridCoord(_data.Origin)}).");
            }
            else
            {
                arg.ReplyWith("Failed to find suitable flat terrain for Trinity Tabernacle.");
            }
        }

        [ConsoleCommand("tabernacle.remove")]
        private void ConsoleRemove(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin)
            {
                return;
            }

            int removed = RemoveExistingChurch();
            arg.ReplyWith($"Removed {removed} entities.");
        }

        [ConsoleCommand("tabernacle.info")]
        private void ConsoleInfo(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin)
            {
                return;
            }

            if (!_data.HasSpawnedThisWipe)
            {
                arg.ReplyWith($"{_config.ChurchName} is not currently spawned.");
                return;
            }

            arg.ReplyWith($"{_config.ChurchName} | Grid: {GetGridCoord(_data.Origin)} | Pos: {_data.Origin} | Entities: {_data.EntityIds.Count}");
        }

        #endregion

        #region Terrain Finder

        private bool SpawnAtBestFlatTerrain()
        {
            if (TerrainMeta.HeightMap == null || TerrainMeta.Size == Vector3.zero)
            {
                PrintWarning("TerrainMeta is not ready; cannot auto-place church.");
                return false;
            }

            float halfMap = (TerrainMeta.Size.x * 0.5f) * Mathf.Clamp(_config.SearchRadiusPercent, 0.1f, 0.85f);
            float step = 35f;

            Vector3 bestPos = Vector3.zero;
            float bestYaw = 0f;
            float bestSlope = float.MaxValue;
            float bestMaxHeight = 0f;
            bool found = false;

            // Spiral-like search outward from map center for the flattest valid spot
            for (float r = 0f; r <= halfMap; r += step)
            {
                int samples = r < 1f ? 1 : Mathf.Max(8, Mathf.FloorToInt(2f * Mathf.PI * r / step));
                for (int i = 0; i < samples; i++)
                {
                    float angle = (i / (float)samples) * Mathf.PI * 2f;
                    float x = Mathf.Cos(angle) * r;
                    float z = Mathf.Sin(angle) * r;
                    var candidate = new Vector3(x, 0f, z);

                    if (!IsNearMonument(candidate, _config.MinMonumentDistance))
                    {
                        foreach (float yaw in new[] { 0f, 90f, 180f, 270f })
                        {
                            if (EvaluateFootprint(candidate, yaw, out float slope, out float maxH))
                            {
                                if (slope < bestSlope)
                                {
                                    bestSlope = slope;
                                    bestMaxHeight = maxH;
                                    bestPos = candidate;
                                    bestYaw = yaw;
                                    found = true;

                                    if (bestSlope <= 0.35f)
                                    {
                                        goto DoneSearching;
                                    }
                                }
                            }
                        }
                    }
                }
            }

        DoneSearching:
            if (!found)
            {
                PrintWarning("Could not find any valid terrain for Trinity Tabernacle.");
                return false;
            }

            if (bestSlope > _config.MaxTerrainSlope)
            {
                PrintWarning($"Best terrain found had slope {bestSlope:F2}m (above configured {_config.MaxTerrainSlope:F2}m), using it as fallback.");
            }

            bestPos.y = bestMaxHeight + 0.35f;
            RemoveExistingChurch();
            SpawnChurch(bestPos, bestYaw);

            Puts($"{_config.ChurchName} spawned at {bestPos} (Grid {GetGridCoord(bestPos)}, slope {bestSlope:F2}m).");
            return true;
        }

        private bool IsNearMonument(Vector3 pos, float minDistance)
        {
            if (TerrainMeta.Path?.Monuments == null)
            {
                return false;
            }

            foreach (var monument in TerrainMeta.Path.Monuments)
            {
                if (monument == null) continue;
                float dist = Vector2.Distance(
                    new Vector2(pos.x, pos.z),
                    new Vector2(monument.transform.position.x, monument.transform.position.z));
                if (dist < minDistance)
                {
                    return true;
                }
            }

            return false;
        }

        private bool EvaluateFootprint(Vector3 origin, float yaw, out float slope, out float maxHeight)
        {
            slope = float.MaxValue;
            maxHeight = 0f;
            float minHeight = float.MaxValue;

            var rot = Quaternion.Euler(0f, yaw, 0f);

            // Sample across the 3x6 foundation footprint (gx in -1..1, gz in -1..5)
            for (int gx = -1; gx <= 1; gx++)
            {
                for (int gz = -1; gz <= 5; gz++)
                {
                    var worldPos = origin + rot * new Vector3(gx * BlockSize, 0f, gz * BlockSize);
                    float h = TerrainMeta.HeightMap.GetHeight(worldPos);
                    float waterDepth = TerrainMeta.WaterMap != null ? TerrainMeta.WaterMap.GetDepth(worldPos) : 0f;

                    if (waterDepth > 0.05f || h < 1.5f)
                    {
                        return false;
                    }

                    if (h < minHeight) minHeight = h;
                    if (h > maxHeight) maxHeight = h;
                }
            }

            slope = maxHeight - minHeight;
            if (slope > Mathf.Max(_config.MaxTerrainSlope, 2.5f))
            {
                return false;
            }

            // Check for large rocks, cliffs, or existing buildings in the footprint
            var center = origin + rot * new Vector3(0f, maxHeight + 3.5f, BlockSize * 2.5f);
            var colliders = Physics.OverlapBox(center, new Vector3(6.5f, 3.0f, 11.5f), rot,
                LayerMask.GetMask("Construction", "Deployed", "World", "Clutter"));

            if (colliders != null && colliders.Length > 0)
            {
                foreach (var col in colliders)
                {
                    if (col == null) continue;
                    // Ignore terrain/ground itself
                    if (col.gameObject.layer == LayerMask.NameToLayer("Terrain")) continue;
                    return false;
                }
            }

            return true;
        }

        #endregion

        #region Church Builder

        private void SpawnChurch(Vector3 origin, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var grade = ParseGrade(_config.BuildingGrade);

            ClearFootprintObstacles(origin, rot);

            _data.Origin = origin;
            _data.RotationY = yaw;
            _data.HasSpawnedThisWipe = true;
            _data.EntityIds.Clear();

            // 1. Foundations: Front steps (0,-1), Porch (0,0), and 3x5 Main Sanctuary (-1..1, 1..5)
            SpawnBlock(PrefabFoundationSteps, origin, rot, new Vector3(0f, 0f, -BlockSize), 180f, grade);
            SpawnBlock(PrefabFoundation, origin, rot, new Vector3(0f, 0f, 0f), 0f, grade);

            for (int gx = -1; gx <= 1; gx++)
            {
                for (int gz = 1; gz <= 5; gz++)
                {
                    SpawnBlock(PrefabFoundation, origin, rot, new Vector3(gx * BlockSize, 0f, gz * BlockSize), 0f, grade);
                }
            }

            // 2. Ground-Floor Exterior Walls (y = 0)
            // Front wall at gz = 1 (facing south / -Z)
            float frontWallZ = 0.5f * BlockSize;
            SpawnBlock(PrefabWallWindow, origin, rot, new Vector3(-BlockSize, 0f, frontWallZ), 180f, grade);
            var frontDoorway = SpawnBlock(PrefabWallDoorway, origin, rot, new Vector3(0f, 0f, frontWallZ), 180f, grade);
            SpawnBlock(PrefabWallWindow, origin, rot, new Vector3(BlockSize, 0f, frontWallZ), 180f, grade);

            if (frontDoorway != null)
            {
                var door = SpawnEntity(PrefabDoorWood, origin, rot, new Vector3(0f, 0f, frontWallZ), 180f) as Door;
                if (door != null)
                {
                    door.SetFlag(BaseEntity.Flags.Open, true);
                }
            }

            // Side walls along gx = -1 (west) and gx = +1 (east) for gz = 1..5
            for (int gz = 1; gz <= 5; gz++)
            {
                string sidePrefab = gz <= 4 ? PrefabWallWindow : PrefabWall;
                float zPos = gz * BlockSize;
                SpawnBlock(sidePrefab, origin, rot, new Vector3(-1.5f * BlockSize, 0f, zPos), -90f, grade);
                SpawnBlock(sidePrefab, origin, rot, new Vector3(1.5f * BlockSize, 0f, zPos), 90f, grade);
            }

            // Back altar wall at gz = 5 (facing north / +Z)
            float backWallZ = 5.5f * BlockSize;
            for (int gx = -1; gx <= 1; gx++)
            {
                SpawnBlock(PrefabWall, origin, rot, new Vector3(gx * BlockSize, 0f, backWallZ), 0f, grade);
            }

            // 3. Porch Roof (y = WallHeight)
            SpawnBlock(PrefabFloor, origin, rot, new Vector3(0f, WallHeight, 0f), 0f, grade);

            // 4. Pitched Roof & Vaulted Nave Ceiling (y = WallHeight to 2 * WallHeight)
            for (int gz = 1; gz <= 5; gz++)
            {
                float zPos = gz * BlockSize;
                // Sloped roofs on left & right aisles
                SpawnBlock(PrefabRoof, origin, rot, new Vector3(-BlockSize, WallHeight, zPos), 90f, grade);
                SpawnBlock(PrefabRoof, origin, rot, new Vector3(BlockSize, WallHeight, zPos), -90f, grade);

                // High vaulted center ceiling at y = 2 * WallHeight (6m)
                SpawnBlock(PrefabFloor, origin, rot, new Vector3(0f, WallHeight * 2f, zPos), 0f, grade);
            }

            // Gable walls at front (gz=1) and back (gz=5) at y = WallHeight
            SpawnBlock(PrefabWallWindow, origin, rot, new Vector3(0f, WallHeight, frontWallZ), 180f, grade);
            SpawnBlock(PrefabWall, origin, rot, new Vector3(0f, WallHeight, backWallZ), 0f, grade);

            // Side clerestory walls along center nave at y = WallHeight? Skip so sloped roofs stay open to the vaulted ceiling!
            // Close the front & back roof triangle gaps on the sides with half walls
            SpawnBlock(PrefabWallHalf, origin, rot, new Vector3(-BlockSize, WallHeight, frontWallZ), 180f, grade);
            SpawnBlock(PrefabWallHalf, origin, rot, new Vector3(BlockSize, WallHeight, frontWallZ), 180f, grade);
            SpawnBlock(PrefabWallHalf, origin, rot, new Vector3(-BlockSize, WallHeight, backWallZ), 0f, grade);
            SpawnBlock(PrefabWallHalf, origin, rot, new Vector3(BlockSize, WallHeight, backWallZ), 0f, grade);

            // 5. Steeple / Bell Tower above front entrance (gx = 0, gz = 1, y = 2 * WallHeight)
            float steepleY = WallHeight * 2f;
            SpawnBlock(PrefabWallWindow, origin, rot, new Vector3(0f, steepleY, frontWallZ), 180f, grade);
            SpawnBlock(PrefabWallWindow, origin, rot, new Vector3(0f, steepleY, 1.5f * BlockSize), 0f, grade);
            SpawnBlock(PrefabWallWindow, origin, rot, new Vector3(-0.5f * BlockSize, steepleY, BlockSize), -90f, grade);
            SpawnBlock(PrefabWallWindow, origin, rot, new Vector3(0.5f * BlockSize, steepleY, BlockSize), 90f, grade);
            SpawnBlock(PrefabFloor, origin, rot, new Vector3(0f, steepleY + WallHeight, BlockSize), 0f, grade);
            SpawnEntity(PrefabLantern, origin, rot, new Vector3(0f, steepleY + 0.1f, BlockSize), 0f);

            // 6. Interior Furnishings: Center Aisle Rugs, Pews (Chairs), Altar Table, Hearth, Lanterns
            for (int gz = 1; gz <= 4; gz++)
            {
                SpawnEntity(PrefabRug, origin, rot, new Vector3(0f, 0.05f, gz * BlockSize), 0f);
            }

            // Pews (2 rows per foundation on left and right sides for gz = 2, 3, 4)
            for (int gz = 2; gz <= 4; gz++)
            {
                float zBase = gz * BlockSize;
                foreach (float zOffset in new[] { -0.7f, 0.7f })
                {
                    // Left pews
                    SpawnEntity(PrefabChair, origin, rot, new Vector3(-BlockSize - 0.6f, 0.05f, zBase + zOffset), 0f);
                    SpawnEntity(PrefabChair, origin, rot, new Vector3(-BlockSize + 0.4f, 0.05f, zBase + zOffset), 0f);

                    // Right pews
                    SpawnEntity(PrefabChair, origin, rot, new Vector3(BlockSize - 0.4f, 0.05f, zBase + zOffset), 0f);
                    SpawnEntity(PrefabChair, origin, rot, new Vector3(BlockSize + 0.6f, 0.05f, zBase + zOffset), 0f);
                }
            }

            // Sanctuary / Altar at gz = 5
            SpawnEntity(PrefabTable, origin, rot, new Vector3(0f, 0.05f, 4.9f * BlockSize), 0f);
            SpawnEntity(PrefabLantern, origin, rot, new Vector3(-0.6f, 0.85f, 4.9f * BlockSize), 0f);
            SpawnEntity(PrefabLantern, origin, rot, new Vector3(0.6f, 0.85f, 4.9f * BlockSize), 0f);

            // Warm Hearth against back wall
            SpawnEntity(PrefabFireplace, origin, rot, new Vector3(0f, 0.05f, 5.35f * BlockSize), 180f);

            // Porch welcome lanterns
            SpawnEntity(PrefabLantern, origin, rot, new Vector3(-1.1f, 0.05f, 0.2f), 0f);
            SpawnEntity(PrefabLantern, origin, rot, new Vector3(1.1f, 0.05f, 0.2f), 0f);

            SaveData();
        }

        private void ClearFootprintObstacles(Vector3 origin, Quaternion rot)
        {
            var center = origin + rot * new Vector3(0f, 1.5f, BlockSize * 2.5f);
            var list = Facepunch.Pool.GetList<BaseEntity>();
            Vis.Entities(center, 15f, list);

            foreach (var ent in list)
            {
                if (ent == null || ent.IsDestroyed) continue;
                if (ent is TreeEntity || ent is ResourceEntity)
                {
                    ent.Kill();
                }
            }

            Facepunch.Pool.FreeList(ref list);
        }

        private BuildingBlock SpawnBlock(string prefab, Vector3 origin, Quaternion originRot, Vector3 localPos, float localYaw, BuildingGrade.Enum grade)
        {
            var worldPos = origin + originRot * localPos;
            var worldRot = originRot * Quaternion.Euler(0f, localYaw, 0f);

            var block = GameManager.server.CreateEntity(prefab, worldPos, worldRot) as BuildingBlock;
            if (block == null)
            {
                PrintWarning($"Failed to create BuildingBlock: {prefab}");
                return null;
            }

            block.gameObject.SetActive(true);
            block.blockDefinition = PrefabAttribute.server.Find<Construction>(block.prefabID);
            block.grounded = true;
            block.Spawn();
            block.SetGrade(grade);
            block.SetHealthToMax();
            block.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);

            if (block.net != null)
            {
                _data.EntityIds.Add(block.net.ID.Value);
            }

            return block;
        }

        private BaseEntity SpawnEntity(string prefab, Vector3 origin, Quaternion originRot, Vector3 localPos, float localYaw)
        {
            var worldPos = origin + originRot * localPos;
            var worldRot = originRot * Quaternion.Euler(0f, localYaw, 0f);

            var entity = GameManager.server.CreateEntity(prefab, worldPos, worldRot);
            if (entity == null)
            {
                PrintWarning($"Failed to create entity: {prefab}");
                return null;
            }

            entity.Spawn();

            if (entity.net != null)
            {
                _data.EntityIds.Add(entity.net.ID.Value);
            }

            if (_config.KeepLightsLit && entity is BaseOven oven)
            {
                LightOven(oven);
            }

            return entity;
        }

        private void LightOven(BaseOven oven)
        {
            if (oven == null || oven.IsDestroyed)
            {
                return;
            }

            if (oven.inventory != null && oven.inventory.itemList.Count == 0)
            {
                var fuelName = oven.ShortPrefabName.Contains("lantern") ? "lowgradefuel" : "wood";
                var fuel = ItemManager.CreateByName(fuelName, 100);
                if (fuel != null)
                {
                    fuel.MoveToContainer(oven.inventory);
                }
            }

            oven.StartCooking();
        }

        private void RefreshLitEntities()
        {
            foreach (var id in _data.EntityIds)
            {
                var entity = BaseNetworkable.serverEntities.Find(new NetworkableId(id)) as BaseOven;
                if (entity != null)
                {
                    LightOven(entity);
                }
            }
        }

        private int RemoveExistingChurch()
        {
            int count = 0;
            foreach (var id in _data.EntityIds.ToList())
            {
                var entity = BaseNetworkable.serverEntities.Find(new NetworkableId(id)) as BaseEntity;
                if (entity != null && !entity.IsDestroyed)
                {
                    entity.Kill();
                    count++;
                }
            }

            _data.EntityIds.Clear();
            _data.HasSpawnedThisWipe = false;
            SaveData();
            return count;
        }

        #endregion

        #region Helpers

        private bool HasAdminPermission(BasePlayer player)
        {
            return player != null && (player.IsAdmin || permission.UserHasPermission(player.UserIDString, PermAdmin));
        }

        private BuildingGrade.Enum ParseGrade(string gradeName)
        {
            if (Enum.TryParse(gradeName, true, out BuildingGrade.Enum parsed))
            {
                return parsed;
            }

            return BuildingGrade.Enum.Stone;
        }

        private string GetGridCoord(Vector3 pos)
        {
            float mapSize = TerrainMeta.Size.x > 0 ? TerrainMeta.Size.x : 4000f;
            float cellSize = 146.3f;
            int gridCount = Mathf.FloorToInt(mapSize / cellSize);

            float offsetX = pos.x + mapSize * 0.5f;
            float offsetZ = mapSize * 0.5f - pos.z;

            int col = Mathf.Clamp(Mathf.FloorToInt(offsetX / cellSize), 0, gridCount - 1);
            int row = Mathf.Clamp(Mathf.FloorToInt(offsetZ / cellSize), 0, gridCount - 1);

            string colLabel = col < 26
                ? ((char)('A' + col)).ToString()
                : $"{(char)('A' + (col / 26 - 1))}{(char)('A' + (col % 26))}";

            return $"{colLabel}{row}";
        }

        #endregion
    }
}
