#region using directives

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using PoGo.NecroBot.Logic.Event;
using PoGo.NecroBot.Logic.PoGoUtils;
using PoGo.NecroBot.Logic.State;
using POGOProtos.Inventory.Item;
using POGOProtos.Map.Fort;
using PoGo.NecroBot.Logic.Utils;
using PoGo.NecroBot.Logic.Logging;
using GeoCoordinatePortable;
using POGOProtos.Networking.Responses;
using POGOProtos.Enums;
using PoGo.NecroBot.Logic.Model.Settings;
using PoGo.NecroBot.Logic.Event.Gym;

#endregion

namespace PoGo.NecroBot.Logic.Tasks
{
    public class VisitNearByGymTask
    {
        private static Dictionary<FortData, DateTime> gyms = new Dictionary<FortData, DateTime>();
        private static ISession _session;
        public static async Task Execute(ISession session, CancellationToken cancellationToken)
        {
            if (!session.LogicSettings.GymAllowed) return;

            _session = session;
            cancellationToken.ThrowIfCancellationRequested();

            FortData nearByGym = GetGymsNearby();
            if (nearByGym != null)
            {
                await VisitGym(session, nearByGym, cancellationToken);
                gyms[nearByGym] = DateTime.Now;
            }
        }

        private static FortData GetGymsNearby()
        {
            List<FortData> results = gyms.Keys.Where(p => gyms[p].AddMinutes(_session.LogicSettings.GymVisitTimeout) < DateTime.Now &&
                                            LocationUtils.CalculateDistanceInMeters(p.Latitude, p.Longitude, _session.Client.CurrentLatitude, _session.Client.CurrentLongitude) < _session.LogicSettings.GymMaxDistance)
                                        .ToList();

            return results.OrderBy(p => LocationUtils.CalculateDistanceInMeters(p.Latitude, p.Longitude, _session.Client.CurrentLatitude, _session.Client.CurrentLongitude) < _session.LogicSettings.GymMaxDistance)
                                        .FirstOrDefault();
        }

        internal static async Task UpdateGymList(ISession session, List<FortData> newGyms)
        {
            var notInList = newGyms.Where(p => !gyms.Keys.Any(x => x.Id == p.Id)).ToList();
            foreach (var item in notInList)
            {
                gyms.Add(item, DateTime.Now.AddDays(-1));
            }

            session.EventDispatcher.Send(new GymListEvent()
            {
                Gyms = newGyms
            });

        }

        private bool CanVisitGym()
        {
            return true;
        }
        private static async Task VisitGym(ISession session, FortData gym, CancellationToken cancelationToken)
        {
            var distance = session.Navigation.WalkStrategy.CalculateDistance(session.Client.CurrentLatitude, session.Client.CurrentLongitude, gym.Latitude, gym.Longitude);

            var fortInfo = await session.Client.Fort.GetFort(gym.Id, gym.Latitude, gym.Longitude);
            if (fortInfo != null)
            {

                //dispatched event to visit gym

                session.EventDispatcher.Send(new GymWalkToTargetEvent()
                {
                    Name = fortInfo.Name,
                    Distance = distance, 
                    Latitude = fortInfo.Latitude, 
                    Longitude = fortInfo.Longitude
                });

                await session.Navigation.Move(new GeoCoordinate(gym.Latitude, gym.Longitude),
                    async () =>
                    {
                        await CatchNearbyPokemonsTask.Execute(session, cancelationToken);
                        return true;
                    },
                    session,
                    cancelationToken);

                var fortDetails = await session.Client.Fort.GetGymDetails(gym.Id, gym.Latitude, gym.Longitude);

                if (fortDetails.Result == GetGymDetailsResponse.Types.Result.Success)
                {
                    if (fortDetails.Result == GetGymDetailsResponse.Types.Result.Success)
                    {
                        var player = session.Profile.PlayerData;

                        if (session.Profile.PlayerData.Team == TeamColor.Neutral)
                        {
                            var defaultTeam = _session.LogicSettings.GymDefaultTeam;
                            var teamResponse = await session.Client.Player.SetPlayerTeam(defaultTeam);
                            if (teamResponse.Status == SetPlayerTeamResponse.Types.Status.Success)
                            {
                                player.Team = defaultTeam;
                            }

                            session.EventDispatcher.Send(new GymTeamJoinEvent()
                            {
                                Team = defaultTeam,
                                Status = teamResponse.Status
                            });
                        }

                        //gym tutorial
                        //if (!player.Team.TutorialState.Contains(TutorialState.GymTutorial))
                        //    await TutorialGeneric(TutorialState.GymTutorial, "GYM");


                        session.EventDispatcher.Send(new GymDetailInfoEvent()
                        {
                            Team = fortDetails.GymState.FortData.OwnedByTeam,
                            Point = gym.GymPoints,
                            Name = fortDetails.Name,
                        });

                        if (player.Team != TeamColor.Neutral && fortDetails.GymState.FortData.OwnedByTeam == player.Team)
                        {
                            var pokemon = await GetDeployablePokemon(session);
                            if (pokemon != null)
                            {
                                var response = await session.Client.Fort.FortDeployPokemon(fortInfo.FortId, pokemon.Id);
                                if (response.Result == FortDeployPokemonResponse.Types.Result.Success)
                                {
                                    session.EventDispatcher.Send(new GymDeployEvent()
                                    {
                                        PokemonId = pokemon.PokemonId,
                                        Name = fortDetails.Name
                                    });

                                }
                            }
                        }
                        else
                        {
                            Logger.Write($"(GYM) Wasted walk on . this gym is defending by other color", LogLevel.None, ConsoleColor.Cyan);
                        }
                    }
                    else
                    {
                        Logger.Write($"(GYM) Not level 5 yet, come back later...", LogLevel.None, ConsoleColor.Cyan);
                    }

                }
            }

            else
            {
                Logger.Write($"(GYM) Ignoring {fortInfo.Name} - ", LogLevel.None, ConsoleColor.Cyan);
            }
        }

        private static async Task<POGOProtos.Data.PokemonData> GetDeployablePokemon(ISession session)
        {
            var pokemonList = (await session.Inventory.GetPokemons()).ToList();
            pokemonList = pokemonList.OrderByDescending(p => p.Cp).Skip(Math.Min(pokemonList.Count - 1, session.LogicSettings.GymNumberOfTopPokemonToBeExcluded)).ToList();


            if (pokemonList.Count == 1) return pokemonList.FirstOrDefault();
            if (_session.LogicSettings.GymUseRandomPokemon)
            {

                return pokemonList.ElementAt(new Random().Next(0, pokemonList.Count - 1));
            }

            var pokemon = pokemonList.FirstOrDefault(p => p.Cp <= _session.LogicSettings.GymMaxCPToDeploy && PokemonInfo.GetLevel(p) <= session.LogicSettings.GymMaxLevelToDeploy && string.IsNullOrEmpty(p.DeployedFortId));
            return pokemon;
        }
    }

}