﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EloBuddy;
using LeagueSharp.Common;
using SharpDX;

namespace ezEvade
{
    public class SpecialSpellEventArgs : EventArgs
    {
        public bool noProcess { get; set; }
    }

    internal class SpellDetector
    {
        public delegate void OnProcessDetectedSpellsHandler();
        public static event OnProcessDetectedSpellsHandler OnProcessDetectedSpells;

        public delegate void OnProcessSpecialSpellHandler(Obj_AI_Base hero, GameObjectProcessSpellCastEventArgs args,
            SpellData spellData, SpecialSpellEventArgs specialSpellArgs);
        public static event OnProcessSpecialSpellHandler OnProcessSpecialSpell;

        //public static event OnDeleteSpellHandler OnDeleteSpell;

        public static Dictionary<int, Spell> spells = new Dictionary<int, Spell>();
        public static Dictionary<int, Spell> drawSpells = new Dictionary<int, Spell>();
        public static Dictionary<int, Spell> detectedSpells = new Dictionary<int, Spell>();

        public static Dictionary<string, ChampionPlugin> championPlugins = new Dictionary<string, ChampionPlugin>();

        public static Dictionary<string, string> channeledSpells = new Dictionary<string, string>();

        public static Dictionary<string, SpellData> onProcessSpells = new Dictionary<string, SpellData>();
        public static Dictionary<string, SpellData> onMissileSpells = new Dictionary<string, SpellData>();

        public static Dictionary<string, SpellData> windupSpells = new Dictionary<string, SpellData>();

        private static int spellIDCount = 0;

        private static AIHeroClient myHero { get { return ObjectManager.Player; } }

        public static float lastCheckTime = 0;
        public static float lastCheckSpellCollisionTime = 0;

        public static Menu menu;
        public static Menu spellMenu;

        public SpellDetector(Menu mainMenu)
        {
            MissileClient.OnCreate += SpellMissile_OnCreate;
            MissileClient.OnDelete += SpellMissile_OnDelete;

            AIHeroClient.OnProcessSpellCast += Game_ProcessSpell;

            Game.OnUpdate += Game_OnGameUpdate;

            menu = mainMenu;

            spellMenu = new Menu("Spells", "Spells");
            menu.AddSubMenu(spellMenu);

            LoadSpellDictionary();
            InitChannelSpells();
        }

        private void SpellMissile_OnCreate(GameObject obj, EventArgs args)
        {
            if (!obj.IsValid<MissileClient>())
                return;

            MissileClient missile = (MissileClient) obj;

            SpellData spellData;

            if (missile.SpellCaster != null && missile.SpellCaster.Team != myHero.Team && 
                missile.SData.Name != null && onMissileSpells.TryGetValue(missile.SData.Name.ToLower(), out spellData)
                && missile.StartPosition != null && missile.EndPosition != null)
            {

                if (missile.StartPosition.Distance(myHero.Position) < spellData.range + 1000)
                {
                    var hero = missile.SpellCaster;

                    if (hero.IsVisible)
                    {
                        if (spellData.usePackets)
                        {
                            CreateSpellData(hero, missile.StartPosition, missile.EndPosition, spellData, obj);
                            return;
                        }

                        var objectAssigned = false;

                        foreach (KeyValuePair<int, Spell> entry in detectedSpells)
                        {
                            Spell spell = entry.Value;

                            var dir = (missile.EndPosition.To2D() - missile.StartPosition.To2D()).Normalized();

                            if (spell.info.isThreeWay == false && spell.info.isSpecial == false)
                            {
                                if (spell.info.missileName == missile.SData.Name.ToLower()) // todo: fix urf spells
                                {
                                    if (spell.heroID == hero.NetworkId && dir.AngleBetween(spell.direction) < 10)
                                    {
                                        spell.spellObject = obj;
                                        objectAssigned = true;
                                        break;
                                    }
                                }
                            }
                        }

                        if (objectAssigned == false)
                        {
                            CreateSpellData(hero, missile.StartPosition, missile.EndPosition, spellData, obj);
                        }
                    }
                    else
                    {
                        if (ObjectCache.menuCache.cache["DodgeFOWSpells"].GetValue<bool>())
                        {
                            CreateSpellData(hero, missile.StartPosition, missile.EndPosition, spellData, obj);
                        }
                    }
                }
            }
        }

        private void SpellMissile_OnDelete(GameObject obj, EventArgs args)
        {
            if (!obj.IsValid<MissileClient>())
                return;

            MissileClient missile = (MissileClient)obj;
            //SpellData spellData;

            foreach (var spell in spells.Values.ToList().Where(
                    s => (s.spellObject != null && s.spellObject.NetworkId == obj.NetworkId))) //isAlive
            {
                //Console.WriteLine("Distance: " + obj.Position.Distance(myHero.Position));

                DelayAction.Add(1, () => DeleteSpell(spell.spellID));
            }
        }      

        public void RemoveNonDangerousSpells()
        {
            foreach (var spell in spells.Values.ToList().Where(
                    s => (s.GetSpellDangerLevel() < 3)))
            {
                DelayAction.Add(1, () => DeleteSpell(spell.spellID));
            }
        }
        private void Game_ProcessSpell(Obj_AI_Base hero, GameObjectProcessSpellCastEventArgs args)
        {
            try
            {
                SpellData spellData;
                if (hero.Team != myHero.Team && onProcessSpells.TryGetValue(args.SData.Name.ToLower(), out spellData))
                {
                    if (spellData.usePackets == false)
                    {
                        var specialSpellArgs = new SpecialSpellEventArgs();
                        OnProcessSpecialSpell?.Invoke(hero, args, spellData, specialSpellArgs);

                        if (specialSpellArgs.noProcess == false && spellData.noProcess == false)
                        {
                            bool foundMissile = false;

                            if (spellData.isThreeWay == false && spellData.isSpecial == false)
                            {
                                foreach (KeyValuePair<int, Spell> entry in detectedSpells)
                                {
                                    Spell spell = entry.Value;

                                    var dir = (args.End.To2D() - args.Start.To2D()).Normalized();

                                    if (spell.spellObject != null)
                                    {
                                        if (spell.info.spellName.ToLower() == args.SData.Name.ToLower()) // todo: fix urf spells
                                        {
                                            if (spell.heroID == hero.NetworkId && dir.AngleBetween(spell.direction) < 10)
                                            {
                                                foundMissile = true;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }

                            if (foundMissile == false || spellData.dontcheckDuplicates)
                            {
                                CreateSpellData(hero, hero.ServerPosition, args.End, spellData, null);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        public static void CreateSpellData(Obj_AI_Base hero, Vector3 spellStartPos, Vector3 spellEndPos,
            SpellData spellData, GameObject obj = null, float extraEndTick = 0.0f, bool processSpell = true,
            SpellType spellType = SpellType.None, bool checkEndExplosion = true,  float spellRadius = 0)
        {
            if (checkEndExplosion && spellData.hasEndExplosion)
            {
                CreateSpellData(hero, spellStartPos, spellEndPos, spellData,
                    obj, extraEndTick, false, spellData.spellType, false);

                var expData = spellData.CopyData();

                if (expData.spellType == SpellType.Line && !expData.name.Contains("_exp"))
                    expData.name = spellData.name + "_exp";

                CreateSpellData(hero, spellStartPos, spellEndPos, expData,
                    obj, extraEndTick, true, SpellType.Circular, false);

                return;
            }

            if (spellStartPos.Distance(myHero.Position) < spellData.range + 1000)
            {
                Vector2 startPosition = spellStartPos.To2D();
                Vector2 endPosition = spellEndPos.To2D();
                Vector2 direction = (endPosition - startPosition).Normalized();
                float endTick = 0;

                if (spellType == SpellType.None)
                {
                    spellType = spellData.spellType;
                }

                if (spellData.fixedRange) //for diana q
                {
                    if (endPosition.Distance(startPosition) > spellData.range)
                        endPosition = startPosition + direction * spellData.range;
                }

                if (spellType == SpellType.Line)
                {
                    endTick = spellData.spellDelay + (spellData.range / spellData.projectileSpeed) * 1000;
                    endPosition = startPosition + direction * spellData.range;

                    if (spellData.useEndPosition)
                    {
                        var range = spellEndPos.To2D().Distance(spellStartPos.To2D());
                        endTick = spellData.spellDelay + (range / spellData.projectileSpeed) * 1000;
                        endPosition = spellEndPos.To2D();
                    }

                    if (obj != null)
                        endTick -= spellData.spellDelay;
                }
                else if (spellType == SpellType.Circular)
                {
                    endTick = spellData.spellDelay;

                    if (spellData.projectileSpeed == 0)
                    {
                        endPosition = hero.ServerPosition.To2D();
                    }
                    else if (spellData.projectileSpeed > 0)
                    {
                        endTick = endTick + 1000 * startPosition.Distance(endPosition) / spellData.projectileSpeed;

                        if (spellData.spellType == SpellType.Line && spellData.hasEndExplosion && !spellData.useEndPosition)
                        {
                            if (ObjectCache.menuCache.cache["CheckSpellCollision"].GetValue<bool>())
                                endPosition = startPosition;
                            else
                                endPosition = startPosition + direction * spellData.range;
                        }
                    }
                }
                else if (spellType == SpellType.Arc)
                {
                    endTick = endTick + 1000 * startPosition.Distance(endPosition) / spellData.projectileSpeed;

                    if (obj != null)
                        endTick -= spellData.spellDelay;
                }
                else if (spellType == SpellType.Cone)
                {
                    return;
                }
                else
                {
                    return;
                }

                if (spellData.invert)
                {
                    var dir = (startPosition - endPosition).Normalized();
                    endPosition = startPosition + dir * startPosition.Distance(endPosition);
                }

                if (spellData.isPerpendicular)
                {
                    startPosition = spellEndPos.To2D() - direction.Perpendicular() * spellData.secondaryRadius;
                    endPosition = spellEndPos.To2D() + direction.Perpendicular() * spellData.secondaryRadius;
                }

                endTick += extraEndTick;

                Spell newSpell = new Spell();

                newSpell.startTime = EvadeUtils.TickCount;
                newSpell.endTime = EvadeUtils.TickCount + endTick;
                newSpell.startPos = startPosition;
                newSpell.endPos = endPosition;
                newSpell.height = spellEndPos.Z + spellData.extraDrawHeight;
                newSpell.direction = direction;
                newSpell.heroID = hero.NetworkId;
                newSpell.info = spellData;
                newSpell.spellType = spellType;
                newSpell.radius = spellRadius > 0 ? spellRadius : newSpell.GetSpellRadius();

                if (obj != null)
                {
                    newSpell.spellObject = obj;
                    newSpell.projectileID = obj.NetworkId;
                }

                int spellID = CreateSpell(newSpell, processSpell);

                DelayAction.Add((int)(endTick + spellData.extraEndTime), () => DeleteSpell(spellID));
            }
        }

        private void Game_OnGameUpdate(EventArgs args)
        {
            UpdateSpells();

            if (EvadeUtils.TickCount - lastCheckSpellCollisionTime > 100)
            {
                CheckSpellCollision();
                lastCheckSpellCollisionTime = EvadeUtils.TickCount;
            }

            if (EvadeUtils.TickCount - lastCheckTime > 1)
            {
                //CheckCasterDead();                
                CheckSpellEndTime();
                AddDetectedSpells();
                lastCheckTime = EvadeUtils.TickCount;
            }
        }

        public static void UpdateSpells()
        {
            foreach (var spell in detectedSpells.Values)
            {
                spell.UpdateSpellInfo();
            }
        }

        private void CheckSpellEndTime()
        {
            foreach (KeyValuePair<int, Spell> entry in detectedSpells)
            {
                Spell spell = entry.Value;

                foreach (var hero in HeroManager.Enemies)
                {
                    if (hero.IsDead && spell.heroID == hero.NetworkId)
                    {
                        if (spell.spellObject == null)
                            DelayAction.Add(1, () => DeleteSpell(entry.Key));
                    }
                }

                if (spell.endTime + spell.info.extraEndTime < EvadeUtils.TickCount
                    || CanHeroWalkIntoSpell(spell) == false)
                {
                    DelayAction.Add(1, () => DeleteSpell(entry.Key));
                }
            }
        }

        private static void CheckSpellCollision()
        {
            if (ObjectCache.menuCache.cache["CheckSpellCollision"].GetValue<bool>() == false)
            {
                return;
            }

            foreach (KeyValuePair<int, Spell> entry in detectedSpells)
            {
                Spell spell = entry.Value;
                var collisionObject = spell.CheckSpellCollision();
                if (collisionObject != null)
                {
                    spell.predictedEndPos = spell.GetSpellProjection(collisionObject.ServerPosition.To2D());

                    var radius = spell.info.name.Contains("_exp") ? spell.info.secondaryRadius : spell.radius;

                    if (spell.currentSpellPosition.Distance(collisionObject.ServerPosition) <
                        collisionObject.BoundingRadius + radius)
                    {
                        DelayAction.Add(1, () => DeleteSpell(entry.Key));
                    }
                }
            }
        }

        public static bool CanHeroWalkIntoSpell(Spell spell)
        {
            if (ObjectCache.menuCache.cache["AdvancedSpellDetection"].GetValue<bool>())
            {
                Vector2 heroPos = myHero.Position.To2D();
                var extraDist = myHero.Distance(ObjectCache.myHeroCache.serverPos2D);

                if (spell.spellType == SpellType.Line)
                {
                    var walkRadius = ObjectCache.myHeroCache.moveSpeed * (spell.endTime - EvadeUtils.TickCount) / 1000 + ObjectCache.myHeroCache.boundingRadius + spell.info.radius + extraDist + 10;
                    var spellPos = spell.currentSpellPosition;
                    var spellEndPos = spell.GetSpellEndPosition();

                    var projection = heroPos.ProjectOn(spellPos, spellEndPos);

                    return projection.SegmentPoint.Distance(heroPos) <= walkRadius;
                }
                else if (spell.spellType == SpellType.Circular)
                {
                    var walkRadius = ObjectCache.myHeroCache.moveSpeed * (spell.endTime - EvadeUtils.TickCount) / 1000 + ObjectCache.myHeroCache.boundingRadius + spell.info.radius + extraDist + 10;

                    if (heroPos.Distance(spell.endPos) < walkRadius)
                    {
                        return true;
                    }

                }
                else if (spell.spellType == SpellType.Arc)
                {
                    var spellRange = spell.startPos.Distance(spell.endPos);
                    var midPoint = spell.startPos + spell.direction * (spellRange / 2);
                    var arcRadius = spell.info.radius * (1 + spellRange / 100);

                    var walkRadius = ObjectCache.myHeroCache.moveSpeed * (spell.endTime - EvadeUtils.TickCount) / 1000 + ObjectCache.myHeroCache.boundingRadius + arcRadius + extraDist + 10;

                    if (heroPos.Distance(midPoint) < walkRadius)
                    {
                        return true;
                    }

                }

                return false;
            }


            return true;
        }

        private static void AddDetectedSpells()
        {
            bool spellAdded = false;

            foreach (KeyValuePair<int, Spell> entry in detectedSpells)
            {
                Spell spell = entry.Value;
                EvadeHelper.fastEvadeMode = ObjectCache.menuCache.cache[spell.info.spellName + "FastEvade"].GetValue<bool>();

                float evadeTime, spellHitTime;
                spell.CanHeroEvade(myHero, out evadeTime, out spellHitTime);

                spell.spellHitTime = spellHitTime;
                spell.evadeTime = evadeTime;

                var extraDelay = ObjectCache.gamePing + ObjectCache.menuCache.cache["ExtraPingBuffer"].GetValue<Slider>().Value;

                if (spell.spellHitTime - extraDelay < 1500 && CanHeroWalkIntoSpell(spell))
                //if(true)
                {
                    Spell newSpell = spell;
                    int spellID = spell.spellID;

                    if (!drawSpells.ContainsKey(spell.spellID))
                    {
                        drawSpells.Add(spellID, newSpell);
                    }

                    //var spellFlyTime = Evade.GetTickCount - spell.startTime;
                    if (spellHitTime < ObjectCache.menuCache.cache["SpellDetectionTime"].GetValue<Slider>().Value
                        && !ObjectCache.menuCache.cache[spell.info.spellName + "FastEvade"].GetValue<bool>())
                    {
                        continue;
                    }

                    if (EvadeUtils.TickCount - spell.startTime < ObjectCache.menuCache.cache["ReactionTime"].GetValue<Slider>().Value
                        && !ObjectCache.menuCache.cache[spell.info.spellName + "FastEvade"].GetValue<bool>())
                    {
                        continue;
                    }

                    var dodgeInterval = ObjectCache.menuCache.cache["DodgeInterval"].GetValue<Slider>().Value;
                    if (Evade.lastPosInfo != null && dodgeInterval > 0)
                    {
                        var timeElapsed = EvadeUtils.TickCount - Evade.lastPosInfo.timestamp;

                        if (dodgeInterval > timeElapsed && !ObjectCache.menuCache.cache[spell.info.spellName + "FastEvade"].GetValue<bool>())
                        {
                            //var delay = dodgeInterval - timeElapsed;
                            //DelayAction.Add((int)delay, () => SpellDetector_OnProcessDetectedSpells());
                            continue;
                        }
                    }

                    if (!spells.ContainsKey(spell.spellID))
                    {
                        if (!(Evade.isDodgeDangerousEnabled() && newSpell.GetSpellDangerLevel() < 3)
                            && ObjectCache.menuCache.cache[newSpell.info.spellName + "DodgeSpell"].GetValue<bool>())
                        {
                            if (newSpell.spellType == SpellType.Circular
                                && ObjectCache.menuCache.cache["DodgeCircularSpells"].GetValue<bool>() == false)
                            {
                                //return spellID;
                                continue;
                            }

                            var healthThreshold =ObjectCache.menuCache.cache[spell.info.spellName + "DodgeIgnoreHP"].GetValue<Slider>() .Value;
                            if (myHero.HealthPercent <= healthThreshold)
                            {
                                spells.Add(spellID, newSpell);
                                spellAdded = true;
                            }
                        }
                    }

                    if (ObjectCache.menuCache.cache["CheckSpellCollision"].GetValue<bool>()
                        && spell.predictedEndPos != Vector2.Zero)
                    {
                        spellAdded = false;
                    }
                }
            }

            if (spellAdded)
            {
                OnProcessDetectedSpells?.Invoke();
            }
        }

        private static int CreateSpell(Spell newSpell, bool processSpell = true)
        {
            int spellID = spellIDCount++;
            newSpell.spellID = spellID;

            newSpell.UpdateSpellInfo();
            detectedSpells.Add(spellID, newSpell);

            if (processSpell)
            {
                CheckSpellCollision();
                AddDetectedSpells();
            }

            return spellID;
        }

        public static void DeleteSpell(int spellID)
        {
            spells.Remove(spellID);
            drawSpells.Remove(spellID);
            detectedSpells.Remove(spellID);
        }

        public static int GetCurrentSpellID()
        {
            return spellIDCount;
        }

        public static List<int> GetSpellList()
        {
            List<int> spellList = new List<int>();

            foreach (KeyValuePair<int, Spell> entry in SpellDetector.spells)
            {
                Spell spell = entry.Value;
                spellList.Add(spell.spellID);
            }

            return spellList;
        }

        public static int GetHighestDetectedSpellID()
        {
            int highest = 0;

            foreach (var spell in SpellDetector.spells)
            {
                highest = Math.Max(highest, spell.Key);
            }

            return highest;
        }

        public static float GetLowestEvadeTime(out Spell lowestSpell)
        {
            float lowest = float.MaxValue;
            lowestSpell = null;

            foreach (KeyValuePair<int, Spell> entry in SpellDetector.spells)
            {
                Spell spell = entry.Value;

                if (spell.spellHitTime != float.MinValue)
                {
                    //Console.WriteLine("spellhittime: " + spell.spellHitTime);
                    lowest = Math.Min(lowest, (spell.spellHitTime - spell.evadeTime));
                    lowestSpell = spell;
                }
            }

            return lowest;
        }

        public static Spell GetMostDangerousSpell(bool hasProjectile = false)
        {
            int maxDanger = 0;
            Spell maxDangerSpell = null;

            foreach (Spell spell in SpellDetector.spells.Values)
            {
                if (!hasProjectile || (spell.info.projectileSpeed > 0 && spell.info.projectileSpeed != float.MaxValue))
                {
                    var dangerlevel = spell.dangerlevel;

                    if (dangerlevel > maxDanger)
                    {
                        maxDanger = dangerlevel;
                        maxDangerSpell = spell;
                    }
                }
            }

            return maxDangerSpell;
        }

        public static void InitChannelSpells()
        {
            channeledSpells["Drain"] = "FiddleSticks";
            channeledSpells["Crowstorm"] = "FiddleSticks";
            channeledSpells["KatarinaR"] = "Katarina";
            channeledSpells["AbsoluteZero"] = "Nunu";
            channeledSpells["GalioIdolOfDurand"] = "Galio";
            channeledSpells["MissFortuneBulletTime"] = "MissFortune";
            channeledSpells["Meditate"] = "MasterYi";
            channeledSpells["NetherGrasp"] = "Malzahar";
            channeledSpells["ReapTheWhirlwind"] = "Janna";
            channeledSpells["KarthusFallenOne"] = "Karthus";
            channeledSpells["KarthusFallenOne2"] = "Karthus";
            channeledSpells["VelkozR"] = "Velkoz";
            channeledSpells["XerathLocusOfPower2"] = "Xerath";
            channeledSpells["ZacE"] = "Zac";
            channeledSpells["Pantheon_Heartseeker"] = "Pantheon";
            channeledSpells["JhinR"] = "Jhin";
            channeledSpells["OdinRecall"] = "AllChampions";
            channeledSpells["Recall"] = "AllChampions";
        }

        public static void LoadDummySpell(SpellData spell)
        {
            string menuName = spell.charName + " (" + spell.spellKey.ToString() + ") Settings";

            var enableSpell = !spell.defaultOff;

            Menu newSpellMenu = new Menu(menuName, spell.charName + spell.spellName + "Settings");
            newSpellMenu.AddItem(new MenuItem(spell.spellName + "DodgeSpell", "Dodge Spell").SetValue(enableSpell)).SetTooltip(spell.name);
            newSpellMenu.AddItem(new MenuItem(spell.spellName + "DrawSpell", "Draw Spell").SetValue(enableSpell));
            newSpellMenu.AddItem(new MenuItem(spell.spellName + "SpellRadius", "Spell Radius")
                .SetValue(new Slider((int) spell.radius, (int) spell.radius - 100, (int) spell.radius + 100)));
            newSpellMenu.AddItem(new MenuItem(spell.spellName+ "FastEvade", "Force Fast Evade"))
                .SetValue(spell.dangerlevel == 4).SetTooltip("Ignores Humanizer Settings & Forces Fast Moveblock.");
            newSpellMenu.AddItem(new MenuItem(spell.spellName + "DodgeIgnoreHP", "Dodge Only Below HP % <="))
                .SetValue(new Slider(spell.dangerlevel == 1 ? 90 : 100));
            newSpellMenu.AddItem(new MenuItem(spell.spellName + "DangerLevel", "Danger Level")
                .SetValue(new StringList(new[] { "Low", "Normal", "High", "Extreme" }, spell.dangerlevel - 1)));

            spellMenu.AddSubMenu(newSpellMenu);

            ObjectCache.menuCache.AddMenuToCache(newSpellMenu);
        }

        //Credits to Kurisu
        public static object NewInstance(Type type)
        {
            var target = type.GetConstructor(Type.EmptyTypes);
            var dynamic = new DynamicMethod(string.Empty, type, new Type[0], target.DeclaringType);
            var il = dynamic.GetILGenerator();

            il.DeclareLocal(target.DeclaringType);
            il.Emit(OpCodes.Newobj, target);
            il.Emit(OpCodes.Stloc_0);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Ret);

            var method = (Func<object>)dynamic.CreateDelegate(typeof(Func<object>));
            return method();
        }

        private void LoadSpecialSpell(SpellData spell)
        {
            if (championPlugins.ContainsKey(spell.charName))
            {
                championPlugins[spell.charName].LoadSpecialSpell(spell);
            }

            championPlugins["AllChampions"].LoadSpecialSpell(spell);
        }

        private void LoadSpecialSpellPlugins()
        {
            championPlugins.Add("AllChampions", new SpecialSpells.AllChampions());

            foreach (var hero in HeroManager.Enemies)
            {
                var championPlugin = Assembly.GetExecutingAssembly()
                    .GetTypes()
                    .Where(t => t.IsClass && t.Namespace == "ezEvade.SpecialSpells"
                               && t.Name == hero.ChampionName
                               ).ToList().FirstOrDefault();

                if (championPlugin != null)
                {
                    if (!championPlugins.ContainsKey(hero.ChampionName))
                    {
                        championPlugins.Add(hero.ChampionName,
                            (ChampionPlugin)NewInstance(championPlugin));
                    }
                }
            }
        }

        private void LoadSpellDictionary()
        {
            LoadSpecialSpellPlugins();

            foreach (var hero in ObjectManager.Get<AIHeroClient>())
            {
                if (hero.IsMe)
                {
                    foreach (var spell in SpellWindupDatabase.Spells.Where(
                        s => (s.charName == hero.ChampionName)))
                    {
                        if (!windupSpells.ContainsKey(spell.spellName))
                        {
                            windupSpells.Add(spell.spellName, spell);
                        }
                    }
                }

                if (hero.Team != myHero.Team)
                {
                    foreach (var spell in SpellDatabase.Spells.Where(
                        s => (s.charName == hero.ChampionName) || (s.charName == "AllChampions")))
                    {
                        //Console.WriteLine(spell.spellName); 

                        if (!(spell.spellType == SpellType.Circular
                            || spell.spellType == SpellType.Line
                            || spell.spellType == SpellType.Arc))
                            continue;

                        if (spell.charName == "AllChampions")
                        {
                            SpellSlot slot = hero.GetSpellSlot(spell.spellName);
                            if (slot == SpellSlot.Unknown)
                            {
                                continue;
                            }
                        }

                        if (!onProcessSpells.ContainsKey(spell.spellName.ToLower()))
                        {
                            if (spell.missileName == "")
                                spell.missileName = spell.spellName;

                            onProcessSpells.Add(spell.spellName.ToLower(), spell);
                            onMissileSpells.Add(spell.missileName.ToLower(), spell);

                            if (spell.extraSpellNames != null)
                            {
                                foreach (string spellName in spell.extraSpellNames)
                                {
                                    onProcessSpells.Add(spellName.ToLower(), spell);
                                }
                            }

                            if (spell.extraMissileNames != null)
                            {
                                foreach (string spellName in spell.extraMissileNames)
                                {
                                    onMissileSpells.Add(spellName.ToLower(), spell);
                                }
                            }

                            LoadSpecialSpell(spell);

                            string menuName = spell.charName + " (" + spell.spellKey.ToString() + ") Settings";

                            var enableSpell = !spell.defaultOff;

                            Menu newSpellMenu = new Menu(menuName, spell.charName + spell.spellName + "Settings");
                            newSpellMenu.AddItem(new MenuItem(spell.spellName + "DodgeSpell", "Dodge Spell").SetValue(enableSpell)).SetTooltip(spell.name);
                            newSpellMenu.AddItem(new MenuItem(spell.spellName + "DrawSpell", "Draw Spell").SetValue(enableSpell));
                            newSpellMenu.AddItem(new MenuItem(spell.spellName + "SpellRadius", "Spell Radius")
                                .SetValue(new Slider((int)spell.radius, (int)spell.radius - 100, (int)spell.radius + 100)));
                            newSpellMenu.AddItem(new MenuItem(spell.spellName + "FastEvade", "Force Fast Evade"))
                                .SetValue(spell.dangerlevel == 4);
                            newSpellMenu.AddItem(new MenuItem(spell.spellName + "DodgeIgnoreHP",
                                "Dodge Only Below HP % <="))
                                .SetValue(new Slider(spell.dangerlevel == 1 ? 90 : 100));
                            newSpellMenu.AddItem(new MenuItem(spell.spellName + "DangerLevel", "Danger Level")
                                .SetValue(new StringList(new[] { "Low", "Normal", "High", "Extreme" }, spell.dangerlevel - 1)));

                            spellMenu.AddSubMenu(newSpellMenu);
                        }
                    }

                }
            }

        }
    }
}
