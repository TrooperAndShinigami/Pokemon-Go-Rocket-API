#region

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AllEnum;
using PokemonGo.RocketAPI.Enums;
using PokemonGo.RocketAPI.Exceptions;
using PokemonGo.RocketAPI.Extensions;
using PokemonGo.RocketAPI.GeneratedCode;

#endregion

namespace PokemonGo.RocketAPI.Console
{
    internal class Program
    {
        private static readonly ISettings ClientSettings = new Settings();

        private static int checkForDuplicates = -1;

        public static void CheckVersion()
        {
            try
            {
                var match =
                    new Regex(
                        @"\[assembly\: AssemblyVersion\(""(\d{1,})\.(\d{1,})\.(\d{1,})\.(\d{1,})""\)\]")
                        .Match(DownloadServerVersion());

                if (!match.Success) return;
                var gitVersion =
                    new Version(
                        string.Format(
                            "{0}.{1}.{2}.{3}",
                            match.Groups[1],
                            match.Groups[2],
                            match.Groups[3],
                            match.Groups[4]));

                if (gitVersion <= Assembly.GetExecutingAssembly().GetName().Version)
                {
                    System.Console.WriteLine("Awesome! You have already got the newest version!");
                    return;
                }
                ;

                System.Console.WriteLine("There is a new Version available: " + gitVersion);
                System.Console.WriteLine("If you have any issues, go get it now.");
                Thread.Sleep(1000);
                //Process.Start("https://github.com/NecronomiconCoding/Pokemon-Go-Rocket-API");
            }
            catch (Exception)
            {
                System.Console.WriteLine("Unable to check for updates now...");
            }
        }

        private static string DownloadServerVersion()
        {
            using (var wC = new WebClient())
                return
                    wC.DownloadString(
                        "https://raw.githubusercontent.com/NecronomiconCoding/Pokemon-Go-Rocket-API/master/PokemonGo/RocketAPI/Console/Properties/AssemblyInfo.cs");
        }

        private static async Task EvolveAllGivenPokemons(Client client, IEnumerable<PokemonData> pokemonToEvolve)
        {
            foreach (var pokemon in pokemonToEvolve)
            {
                /*
                enum Holoholo.Rpc.Types.EvolvePokemonOutProto.Result {
	                UNSET = 0;
	                SUCCESS = 1;
	                FAILED_POKEMON_MISSING = 2;
	                FAILED_INSUFFICIENT_RESOURCES = 3;
	                FAILED_POKEMON_CANNOT_EVOLVE = 4;
	                FAILED_POKEMON_IS_DEPLOYED = 5;
                }
                }*/

                var countOfEvolvedUnits = 0;
                var xpCount = 0;

                EvolvePokemonOut evolvePokemonOutProto;
                do
                {
                    evolvePokemonOutProto = await client.EvolvePokemon(pokemon.Id);
                        //todo: someone check whether this still works

                    if (evolvePokemonOutProto.Result == 1)
                    {
                        System.Console.WriteLine(
                            $"Evolved {pokemon.PokemonId} successfully for {evolvePokemonOutProto.ExpAwarded}xp");

                        countOfEvolvedUnits++;
                        xpCount += evolvePokemonOutProto.ExpAwarded;
                    }
                    else
                    {
                        var result = evolvePokemonOutProto.Result;
                        /*
                        System.Console.WriteLine($"Failed to evolve {pokemon.PokemonId}. " +
                                                 $"EvolvePokemonOutProto.Result was {result}");

                        System.Console.WriteLine($"Due to above error, stopping evolving {pokemon.PokemonId}");
                        */
                    }
                } while (evolvePokemonOutProto.Result == 1);
                if (countOfEvolvedUnits > 0)
                    System.Console.WriteLine(
                        $"Evolved {countOfEvolvedUnits} pieces of {pokemon.PokemonId} for {xpCount}xp");

                await Task.Delay(3000);
            }
        }

        private static async void Execute()
        {
            try
            {
                var client = new Client(ClientSettings);

                if (ClientSettings.AuthType == AuthType.Ptc)
                    await client.DoPtcLogin(ClientSettings.PtcUsername, ClientSettings.PtcPassword);
                else if (ClientSettings.AuthType == AuthType.Google)
                    await client.DoGoogleLogin();

                await client.SetServer();
                var profile = await client.GetProfile();
                var settings = await client.GetSettings();
                var mapObjects = await client.GetMapObjects();
                var inventory = await client.GetInventory();

                await ExecuteFarmingPokestopsAndPokemons(client);
            }
            catch (TaskCanceledException tce) { System.Console.WriteLine("Task Canceled Exception - Restarting"); Execute(); }
            catch (UriFormatException ufe) { System.Console.WriteLine("System URI Format Exception - Restarting"); Execute(); }
            catch (ArgumentOutOfRangeException aore) { System.Console.WriteLine("ArgumentOutOfRangeException - Restarting"); Execute(); }
            catch (NullReferenceException nre) { System.Console.WriteLine("Null Refference - Restarting"); Execute(); }

            //await ExecuteCatchAllNearbyPokemons(client);
        }

        private static async Task EvolveAndTransfer(Client client)
        {
            var inventory = await client.GetInventory();
            var pokemons =
                    inventory.InventoryDelta.InventoryItems.Select(i => i.InventoryItemData?.Pokemon)
                        .Where(p => p != null && p?.PokemonId > 0);

            if (ClientSettings.TransferType == "leaveStrongest")
                await TransferAllButStrongestUnwantedPokemon(client);
            else if (ClientSettings.TransferType == "all")
                await TransferAllGivenPokemons(client, pokemons);
            else if (ClientSettings.TransferType == "duplicate")
                await TransferDuplicatePokemon(client);
            else if (ClientSettings.TransferType == "cp")
                await TransferAllWeakPokemon(client, ClientSettings.TransferCPThreshold);
            else
                System.Console.WriteLine("Transfering pokemon disabled");
            if (ClientSettings.EvolveAllGivenPokemons)
                await EvolveAllGivenPokemons(client, pokemons);
        }

        private static async Task ExecuteCatchAllNearbyPokemons(Client client)
        {
            var mapObjects = await client.GetMapObjects();

            var pokemons = mapObjects.MapCells.SelectMany(i => i.CatchablePokemons);

            foreach (var pokemon in pokemons)
            {
                var update = await client.UpdatePlayerLocation(pokemon.Latitude, pokemon.Longitude);
                var encounterPokemonResponse = await client.EncounterPokemon(pokemon.EncounterId, pokemon.SpawnpointId);
                var pokemonCP = encounterPokemonResponse?.WildPokemon?.PokemonData?.Cp;
                CatchPokemonResponse caughtPokemonResponse;
                do
                {
                    caughtPokemonResponse =
                        await
                            client.CatchPokemon(pokemon.EncounterId, pokemon.SpawnpointId, pokemon.Latitude,
                                pokemon.Longitude, MiscEnums.Item.ITEM_POKE_BALL, pokemonCP);
                    ; //note: reverted from settings because this should not be part of settings but part of logic
                } while (caughtPokemonResponse.Status == CatchPokemonResponse.Types.CatchStatus.CatchMissed);
                System.Console.WriteLine(caughtPokemonResponse.Status ==
                                         CatchPokemonResponse.Types.CatchStatus.CatchSuccess
                    ? $"[{DateTime.Now.ToString("HH:mm:ss")}] We caught a {pokemon.PokemonId} with CP {pokemonCP}"
                    : $"[{DateTime.Now.ToString("HH:mm:ss")}] {pokemon.PokemonId} with CP {pokemonCP} got away..");

                await Task.Delay(3500);
            }
        }

        private static async Task ExecuteFarmingPokestopsAndPokemons(Client client)
        {
            int counter = 0;
            var mapObjects = await client.GetMapObjects();

            var pokeStops =
                mapObjects.MapCells.SelectMany(i => i.Forts)
                    .Where(
                        i =>
                            i.Type == FortType.Checkpoint &&
                            i.CooldownCompleteTimestampMs < DateTime.UtcNow.ToUnixTime());

            foreach (var pokeStop in pokeStops)
            {
                var update = await client.UpdatePlayerLocation(pokeStop.Latitude, pokeStop.Longitude);
                var fortInfo = await client.GetFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);
                var fortSearch = await client.SearchFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);

                System.Console.WriteLine(
                    $"[{DateTime.Now.ToString("HH:mm:ss")}] Farmed XP: {fortSearch.ExperienceAwarded}, Gems: {fortSearch.GemsAwarded}, Eggs: {fortSearch.PokemonDataEgg} Items: {GetFriendlyItemsString(fortSearch.ItemsAwarded)}");

                await Task.Delay(4000);
                await ExecuteCatchAllNearbyPokemons(client);
                await Task.Delay(6000);
                await EvolveAndTransfer(client);
            }
        }

        private static string GetFriendlyItemsString(IEnumerable<FortSearchResponse.Types.ItemAward> items)
        {
            var enumerable = items as IList<FortSearchResponse.Types.ItemAward> ?? items.ToList();

            if (!enumerable.Any())
                return string.Empty;

            return
                enumerable.GroupBy(i => i.ItemId)
                    .Select(kvp => new {ItemName = kvp.Key.ToString(), Amount = kvp.Sum(x => x.ItemCount)})
                    .Select(y => $"{y.Amount} x {y.ItemName}")
                    .Aggregate((a, b) => $"{a}, {b}");
        }

        private static void Main(string[] args)
        {
            Task.Run(() =>
            {
                try
                {
                    System.Console.WriteLine("Coded by Ferox - edited by NecronomiconCoding");
                    CheckVersion();
                    Execute();
                }
                catch (PtcOfflineException)
                {
                    System.Console.WriteLine("PTC Servers are probably down OR your credentials are wrong. Try google");
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"Unhandled exception: {ex}");
                }
            });
            System.Console.ReadLine();
        }

        private static async Task TransferAllButStrongestUnwantedPokemon(Client client)
        {
            System.Console.WriteLine("[!] firing up the meat grinder");

            var unwantedPokemonTypes = new[]
            {
                PokemonId.Pidgey,
                PokemonId.Rattata,
                PokemonId.Weedle,
                PokemonId.Zubat,
                PokemonId.Caterpie,
                PokemonId.Pidgeotto,
                PokemonId.NidoranFemale,
                PokemonId.Paras,
                PokemonId.Venonat,
                PokemonId.Psyduck,
                PokemonId.Poliwag,
                PokemonId.Slowpoke,
                PokemonId.Drowzee,
                PokemonId.Gastly,
                PokemonId.Goldeen,
                PokemonId.Staryu,
                PokemonId.Magikarp,
                PokemonId.Eevee,
                PokemonId.Dratini
            };

            var inventory = await client.GetInventory();
            var pokemons = inventory.InventoryDelta.InventoryItems
                .Select(i => i.InventoryItemData?.Pokemon)
                .Where(p => p != null && p?.PokemonId > 0)
                .ToArray();

            foreach (var unwantedPokemonType in unwantedPokemonTypes)
            {
                var pokemonOfDesiredType = pokemons.Where(p => p.PokemonId == unwantedPokemonType)
                    .OrderByDescending(p => p.Cp)
                    .ToList();

                var unwantedPokemon =
                    pokemonOfDesiredType.Skip(1) // keep the strongest one for potential battle-evolving
                        .ToList();
                if (unwantedPokemon.Count > 0)
                {
                    System.Console.WriteLine($"Grinding {unwantedPokemon.Count} pokemons of type {unwantedPokemonType}");
                    await TransferAllGivenPokemons(client, unwantedPokemon);
                }
            }

            System.Console.WriteLine("[!] finished grinding all the meat");
        }

        private static async Task TransferAllGivenPokemons(Client client, IEnumerable<PokemonData> unwantedPokemons)
        {
            foreach (var pokemon in unwantedPokemons)
            {
                var transferPokemonResponse = await client.TransferPokemon(pokemon.Id);

                /*
                ReleasePokemonOutProto.Status {
	                UNSET = 0;
	                SUCCESS = 1;
	                POKEMON_DEPLOYED = 2;
	                FAILED = 3;
	                ERROR_POKEMON_IS_EGG = 4;
                }*/

                if (transferPokemonResponse.Status == 1)
                {
                    System.Console.WriteLine($"Shoved another {pokemon.PokemonId} down the meat grinder");
                }
                else
                {
                    var status = transferPokemonResponse.Status;

                    System.Console.WriteLine($"Somehow failed to grind {pokemon.PokemonId}. " +
                                             $"ReleasePokemonOutProto.Status was {status}");
                }

                await Task.Delay(3000);
            }
        }

        private static async Task TransferDuplicatePokemon(Client client)
        {
            checkForDuplicates++;
            if (checkForDuplicates%2 == 0)
            {
                checkForDuplicates = 0;
                System.Console.WriteLine($"Check for duplicates");
                var inventory = await client.GetInventory();
                var allpokemons =
                    inventory.InventoryDelta.InventoryItems.Select(i => i.InventoryItemData?.Pokemon)
                        .Where(p => p != null && p?.PokemonId > 0);

                var dupes = allpokemons.OrderBy(x => x.Cp).Select((x, i) => new {index = i, value = x})
                    .GroupBy(x => x.value.PokemonId)
                    .Where(x => x.Skip(1).Any());

                for (var i = 0; i < dupes.Count(); i++)
                {
                    for (var j = 0; j < dupes.ElementAt(i).Count() - 1; j++)
                    {
                        var dubpokemon = dupes.ElementAt(i).ElementAt(j).value;
                        var transfer = await client.TransferPokemon(dubpokemon.Id);
                        System.Console.WriteLine(
                            $"Transfer {dubpokemon.PokemonId} with {dubpokemon.Cp} CP (highest has {dupes.ElementAt(i).Last().value.Cp})");
                    }
                }
            }
        }

        private static async Task TransferAllWeakPokemon(Client client, int cpThreshold)
        {
            System.Console.WriteLine("[!] firing up the meat grinder");

            var doNotTransfer = new[] //these will not be transferred even when below the CP threshold
            {
                //PokemonId.Pidgey,
                //PokemonId.Rattata,
                //PokemonId.Weedle,
                //PokemonId.Zubat,
                //PokemonId.Caterpie,
                //PokemonId.Pidgeotto,
                //PokemonId.NidoranFemale,
                //PokemonId.Paras,
                //PokemonId.Venonat,
                //PokemonId.Psyduck,
                //PokemonId.Poliwag,
                //PokemonId.Slowpoke,
                //PokemonId.Drowzee,
                //PokemonId.Gastly,
                //PokemonId.Goldeen,
                //PokemonId.Staryu,
                PokemonId.Magikarp,
                PokemonId.Eevee//,
                //PokemonId.Dratini
            };

            var inventory = await client.GetInventory();
            var pokemons = inventory.InventoryDelta.InventoryItems
                                .Select(i => i.InventoryItemData?.Pokemon)
                                .Where(p => p != null && p?.PokemonId > 0)
                                .ToArray();

            //foreach (var unwantedPokemonType in unwantedPokemonTypes)
            {
                var pokemonToDiscard = pokemons.Where(p => !doNotTransfer.Contains(p.PokemonId) && p.Cp < cpThreshold)
                                                   .OrderByDescending(p => p.Cp)
                                                   .ToList();

                //var unwantedPokemon = pokemonOfDesiredType.Skip(1) // keep the strongest one for potential battle-evolving
                //                                          .ToList();
                if (pokemonToDiscard.Count > 0)
                {
                    System.Console.WriteLine($"Grinding {pokemonToDiscard.Count} pokemon below {cpThreshold} CP.");
                    await TransferAllGivenPokemons(client, pokemonToDiscard);
                }
            }

            System.Console.WriteLine("[!] finished grinding all the meat");
        }
    }
}