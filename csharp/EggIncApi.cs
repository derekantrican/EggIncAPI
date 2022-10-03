using Ei;
using Google.Protobuf;
using static Ei.Backup.Types;

namespace EggIncApi;
public class EggIncApi
{
    private const int CLIENTVERSION = 42;
    public static async Task<ContractCoopStatusResponse> GetCoopStatus(string contractId, string coopId, string userId)
    {
        ContractCoopStatusRequest coopStatusRequest = new ContractCoopStatusRequest();
        coopStatusRequest.ContractIdentifier = contractId;
        coopStatusRequest.CoopIdentifier = coopId;
        coopStatusRequest.UserId = userId;

        return await MakeEggIncApiRequest("coop_status", coopStatusRequest, ContractCoopStatusResponse.Parser.ParseFrom);
    }

    public static async Task<EggIncFirstContactResponse> GetFirstContact(string userId)
    {
        EggIncFirstContactRequest firstContactRequest = new EggIncFirstContactRequest();
        firstContactRequest.EiUserId = userId;
        firstContactRequest.ClientVersion = CLIENTVERSION;

        return await MakeEggIncApiRequest("bot_first_contact", firstContactRequest, EggIncFirstContactResponse.Parser.ParseFrom, false);
    }

    public static async Task<PeriodicalsResponse> GetPeriodicals(string userId)
    {
        GetPeriodicalsRequest getPeriodicalsRequest = new GetPeriodicalsRequest();
        getPeriodicalsRequest.UserId = userId;
        getPeriodicalsRequest.CurrentClientVersion = CLIENTVERSION;

        return await MakeEggIncApiRequest("get_periodicals", getPeriodicalsRequest, PeriodicalsResponse.Parser.ParseFrom);
    }

    private static async Task<T> MakeEggIncApiRequest<T>(string endpoint, IMessage data, Func<ByteString, T> parseMethod, bool authenticated = true)
    {
        byte[] bytes;
        using (var stream = new MemoryStream())
        {
            data.WriteTo(stream);
            bytes = stream.ToArray();
        }

        string response = await PostRequest($"https://www.auxbrain.com/ei/{endpoint}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "data", Convert.ToBase64String(bytes) }
        }));

        if (authenticated)
        {
            AuthenticatedMessage authenticatedMessage = AuthenticatedMessage.Parser.ParseFrom(Convert.FromBase64String(response));
            return parseMethod(authenticatedMessage.Message);
        }
        else
        {
            return parseMethod(ByteString.CopyFrom(Convert.FromBase64String(response)));
        }
    }

    public static async Task<bool> IsCoopContractOnTrack(LocalContract contract)
    {
        var coopStatusResponse = await EggIncApi.GetCoopStatus(contract.Contract.Identifier, contract.CoopIdentifier, contract.CoopUserId);
        var totalEggsPerHour = coopStatusResponse.Contributors.Sum(c => c.ContributionRate * 3600);
        var targetAmount = contract.Contract.Goals.Last().TargetAmount;
        var requiredEggsPerHour = ((targetAmount - coopStatusResponse.TotalAmount) / coopStatusResponse.SecondsRemaining) * 3600;

        return totalEggsPerHour >= requiredEggsPerHour;
    }

    public static bool IsSoloContractOnTrack(LocalContract contract, Backup backup)
    {
        //For a solo contract, we cannot simply get the "contribution_rate" of the player (because we don't have coop_status_response).
        //So we have to calculate manually using researches, artifacts, etc

        int farmIndex = -1;
        Simulation contractFarm = null;
        for (int i = 0; i < backup.Farms.Count; i++)
        {
            if (backup.Farms[i].ContractId == contract.Contract.Identifier)
            {
                contractFarm = backup.Farms[i];
                farmIndex = i;
                break;
            }
        }

        //ARTIFACTS
        Dictionary<ArtifactSpec.Types.Name, double[][]> artifactMultipliers = new Dictionary<ArtifactSpec.Types.Name, double[][]>
        {
            { 
                ArtifactSpec.Types.Name.QuantumMetronome, 
                new[]
                {
                    new[] { 0.05 }, //Misaligned
                    new[] { 0.1, 0.12 }, //Adequate
                    new[] { 0.15, 0.17, 0.2 }, //Perfect
                    new[] { 0.25, 0.27, 0.3, 0.35 }, //Reggference
                }
            },
            { 
                ArtifactSpec.Types.Name.InterstellarCompass, 
                new[]
                {
                    new[] { 0.05 }, //Miscalibrated
                    new[] { 0.1 }, //Regular
                    new[] { 0.2, 0.22 }, //Precise
                    new[] { 0.3, 0.35, 0.4, 0.5 }, //Clairvoyant
                }
            },
            { 
                ArtifactSpec.Types.Name.TachyonStone, 
                new[]
                {
                    //Fragment cannot be set (no level)
                    new[] { 0.02 }, //Regular
                    new[] { 0.04 }, //Eggsquisite
                    new[] { 0.05 }, //Brilliant
                }
            },
            { 
                ArtifactSpec.Types.Name.QuantumStone, 
                new[]
                {
                    //Fragment cannot be set (no level)
                    new[] { 0.02 }, //Regular
                    new[] { 0.04 }, //Phased
                    new[] { 0.05 }, //Meggnificient
                }
            },
        };

        var artifactInventory = backup.ArtifactsDb.InventoryItems;
        var contractFarmArtifactSlots = backup.ArtifactsDb.ActiveArtifactSets[farmIndex].Slots;

        double eggLayingRateArtifactMultiplier = 1;
        double shippingRateArtifactMultiplier = 1;
        foreach (var artifactSlot in contractFarmArtifactSlots)
        {
            if (artifactSlot.Occupied)
            {
                var artifact = artifactInventory.FirstOrDefault(a => a.ItemId == artifactSlot.ItemId);
                if (artifact.Artifact.Spec.Name == ArtifactSpec.Types.Name.QuantumMetronome)
                {
                    var artifactMultiplier = artifactMultipliers[artifact.Artifact.Spec.Name][(int)artifact.Artifact.Spec.Level][(int)artifact.Artifact.Spec.Rarity];
                    eggLayingRateArtifactMultiplier *= 1 + artifactMultiplier;
                }
                else if (artifact.Artifact.Spec.Name == ArtifactSpec.Types.Name.QuantumMetronome)
                {
                    var artifactMultiplier = artifactMultipliers[artifact.Artifact.Spec.Name][(int)artifact.Artifact.Spec.Level][(int)artifact.Artifact.Spec.Rarity];
                    shippingRateArtifactMultiplier *= 1 + artifactMultiplier;
                }

                foreach (var stone in artifact.Artifact.Stones)
                {
                    if (stone.Name == ArtifactSpec.Types.Name.TachyonStone)
                    {
                        var stoneMultiplier = artifactMultipliers[stone.Name][(int)stone.Level][0];
                        eggLayingRateArtifactMultiplier *= 1 + stoneMultiplier;
                    }
                    else if (stone.Name == ArtifactSpec.Types.Name.QuantumStone)
                    {
                        var stoneMultiplier = artifactMultipliers[stone.Name][(int)stone.Level][0];
                        shippingRateArtifactMultiplier *= 1 + stoneMultiplier;
                    }
                }
            }
        }

        //EGG LAYING RATE
        Dictionary<string, double> eggLayingResearchMultipliersPerLevel = new Dictionary<string, double>
        {
            { "comfy_nests", 0.1 },
            { "hen_house_ac", 0.05 },
            { "improved_genetics", 0.15 },
            { "time_compress", 0.1 },
            { "timeline_diversion", 0.02 },
            { "relativity_optimization", 0.1 },
            { "epic_egg_laying", 0.05 },
        };

        double eggLayingRateResearchMultiplier = 1;
        foreach (var commonResearch in contractFarm.CommonResearch)
        {
            if (eggLayingResearchMultipliersPerLevel.ContainsKey(commonResearch.Id))
            {
                eggLayingRateResearchMultiplier *= 1 + eggLayingResearchMultipliersPerLevel[commonResearch.Id] * commonResearch.Level;
            }
        }

        var epicComfyNestsLevel = backup.Game.EpicResearch.FirstOrDefault(er => er.Id == "epic_egg_laying").Level;
        eggLayingRateResearchMultiplier *= 1 + eggLayingResearchMultipliersPerLevel["epic_egg_laying"] * epicComfyNestsLevel;

        var layableEggsPerSecond = contractFarm.NumChickens * 1/30 * eggLayingRateResearchMultiplier * eggLayingRateArtifactMultiplier;

        //SHIPPING CAPACITY
        var vehicleCapacities = new List<double>();
        for (int i = 0; i < contractFarm.Vehicles.Count; i++)
        {
            if (contractFarm.Vehicles[i] != 11) //Calculation is based off all vehicles being hyperloops
            {
                Console.WriteLine("  Found vehicle that is not a hyperloop. These calculations are all based on hyperloops so these calculations will not be accurate...");
            }
            else
            {
                vehicleCapacities.Add((50e6/60) * contractFarm.TrainLength[i]);
            }
        }

        Dictionary<string, double> shippingRateResearchMultipliersPerLevel = new Dictionary<string, double>
        {
            { "leafsprings" , 0.05 },
            { "lightweight_boxes" , 0.1 },
            { "driver_training" , 0.05 },
            { "super_alloy" , 0.05 },
            { "quantum_storage" , 0.05 },
            { "hover_upgrades" , 0.05 }, //Only valid for hover vehicles (but our calculations assume that all vehicles are hyperloops)
            { "dark_containment" , 0.05 },
            { "neural_net_refine" , 0.05 },
            { "hyper_portalling" , 0.05 }, //Only valid for hyperloops (but our calculations assume that all vehicles are hyperloops)
            { "transportation_lobbyist" , 0.05 },
        };

        double shippingRateResearchMultiplier = 1;
        foreach (var commonResearch in contractFarm.CommonResearch)
        {
            if (shippingRateResearchMultipliersPerLevel.ContainsKey(commonResearch.Id))
            {
                shippingRateResearchMultiplier *= 1 + shippingRateResearchMultipliersPerLevel[commonResearch.Id] * commonResearch.Level;
            }
        }

        var transportationLobbyistLevel = backup.Game.EpicResearch.FirstOrDefault(er => er.Id == "transportation_lobbyist").Level;
        shippingRateResearchMultiplier *= 1 + shippingRateResearchMultipliersPerLevel["transportation_lobbyist"] * transportationLobbyistLevel;

        var shippableEggsPerSecond = vehicleCapacities.Sum(v => v * shippingRateResearchMultiplier * shippingRateArtifactMultiplier);

        //FINAL CALCULATION
        var targetAmount = contract.Contract.Goals.Last().TargetAmount;
        var contractEndTime = contract.TimeAccepted + contract.Contract.LengthSeconds;
        var secondsRemaining = contractEndTime - new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds();

        var requiredEggsPerHour = ((targetAmount - contractFarm.EggsLaid) / secondsRemaining) * 3600;
        var eggsPerHour = Math.Min(layableEggsPerSecond, shippableEggsPerSecond) * 3600;

        return eggsPerHour >= requiredEggsPerHour;
    }

    public static double CalcuateOfflineInternalHatcheryRate(Simulation farm, Backup backup)
    {
        //ARTIFACTS
        Dictionary<ArtifactSpec.Types.Name, double[][]> artifactMultipliers = new Dictionary<ArtifactSpec.Types.Name, double[][]>
        {
            { 
                ArtifactSpec.Types.Name.TheChalice, 
                new[]
                {
                    new[] { 0.05 }, //Plain
                    new[] { 0.1, 0.0, 0.15 }, //Polished (note: according to the wiki, "rare" apparently isn't possible)
                    new[] { 0.2, 0.23, 0.25 }, //Jeweled
                    new[] { 0.30, 0.0, 0.35, 0.4 }, //Eggceptional (note: according to the wiki, "rare" apparently isn't possible)
                }
            },
            { 
                ArtifactSpec.Types.Name.LifeStone, 
                new[]
                {
                    //Fragment cannot be set (no level)
                    new[] { 0.02 }, //Regular
                    new[] { 0.03 }, //Good
                    new[] { 0.04 }, //Eggceptional
                }
            },
            { 
                ArtifactSpec.Types.Name.ClarityStone, 
                new[]
                {
                    //Fragment cannot be set (no level)
                    new[] { 0.25 }, //Regular
                    new[] { 0.5 }, //Eggsquisite
                    new[] { 1.0 }, //Eggceptional
                }
            },
        };

        var artifactInventory = backup.ArtifactsDb.InventoryItems;
        var contractFarmArtifactSlots = backup.ArtifactsDb.ActiveArtifactSets[0].Slots;

        double internalHatcheryRateArtifactMultiplier = 1;
        foreach (var artifactSlot in contractFarmArtifactSlots)
        {
            if (artifactSlot.Occupied)
            {
                var artifact = artifactInventory.FirstOrDefault(a => a.ItemId == artifactSlot.ItemId);
                if (artifact.Artifact.Spec.Name == ArtifactSpec.Types.Name.TheChalice)
                {
                    var artifactMultiplier = artifactMultipliers[artifact.Artifact.Spec.Name][(int)artifact.Artifact.Spec.Level][(int)artifact.Artifact.Spec.Rarity];

                    if (farm.EggType == Ei.Egg.Enlightenment)
                    {
                        double clarityStoneMultiplier = 0;
                        foreach (var stone in artifact.Artifact.Stones)
                        {
                            if (stone.Name == ArtifactSpec.Types.Name.ClarityStone)
                            {
                                clarityStoneMultiplier += artifactMultipliers[stone.Name][(int)stone.Level][0];
                            }
                        }

                        artifactMultiplier *= clarityStoneMultiplier;
                    }

                    internalHatcheryRateArtifactMultiplier *= 1 + artifactMultiplier;
                }

                foreach (var stone in artifact.Artifact.Stones)
                {
                    if (stone.Name == ArtifactSpec.Types.Name.LifeStone)
                    {
                        var stoneMultiplier = artifactMultipliers[stone.Name][(int)stone.Level][0];
                        internalHatcheryRateArtifactMultiplier *= 1 + stoneMultiplier;
                    }
                }
            }
        }

        //EGG LAYING RATE
        Dictionary<string, double> internalHatcheryResearchMultipliersPerLevel = new Dictionary<string, double>
        {
            { "internal_hatchery1", 2 },
            { "internal_hatchery2", 5 },
            { "internal_hatchery3", 10 },
            { "internal_hatchery4", 25 },
            { "internal_hatchery5", 5 },
            { "neural_linking", 50 },
            { "epic_internal_incubators", 0.05 },
            { "int_hatch_calm", 0.1 }
            //Assumed that the epic research "Internal Hatchery Sharing" does not come into play (this is what wasmegg does)
        };

        double internalHatcheryRateResearchMultiplier = 0;
        foreach (var commonResearch in farm.CommonResearch)
        {
            if (internalHatcheryResearchMultipliersPerLevel.ContainsKey(commonResearch.Id))
            {
                //Common researches for IHR are additive
                internalHatcheryRateResearchMultiplier += internalHatcheryResearchMultipliersPerLevel[commonResearch.Id] * commonResearch.Level;
            }
        }

        //Epic researches for IHR are compounding
        var epicInternalHatcheriesLevel = backup.Game.EpicResearch.FirstOrDefault(er => er.Id == "epic_internal_incubators").Level;
        internalHatcheryRateResearchMultiplier *= 1 + internalHatcheryResearchMultipliersPerLevel["epic_internal_incubators"] * epicInternalHatcheriesLevel;

        var internalHatcheryCalmLevel = backup.Game.EpicResearch.FirstOrDefault(er => er.Id == "int_hatch_calm").Level;
        internalHatcheryRateResearchMultiplier *= 1 + internalHatcheryResearchMultipliersPerLevel["int_hatch_calm"] * internalHatcheryCalmLevel;

        return internalHatcheryRateArtifactMultiplier * internalHatcheryRateResearchMultiplier * 4; //Assume 4 habs (this is what wasmegg does)
    }

    public static double CalculateHabSpace(Simulation farm, Backup backup)
    {
        //ARTIFACTS
        Dictionary<ArtifactSpec.Types.Name, double[][]> artifactMultipliers = new Dictionary<ArtifactSpec.Types.Name, double[][]>
        {
            { 
                ArtifactSpec.Types.Name.OrnateGusset, 
                new[]
                {
                    new[] { 0.05 }, //Plain
                    new[] { 0.1, 0.12 }, //Ornate
                    new[] { 0.15, 0.16 }, //Distegguished
                    new[] { 0.2, 0.0, 0.22, 0.25, 0.4 }, //Jeweled (note: according to the wiki, "rare" apparently isn't possible)
                }
            },
            { 
                ArtifactSpec.Types.Name.ClarityStone, 
                new[]
                {
                    //Fragment cannot be set (no level)
                    new[] { 0.25 }, //Regular
                    new[] { 0.5 }, //Eggsquisite
                    new[] { 1.0 }, //Eggceptional
                }
            },
        };

        var artifactInventory = backup.ArtifactsDb.InventoryItems;
        var contractFarmArtifactSlots = backup.ArtifactsDb.ActiveArtifactSets[0].Slots;

        double habCapacityArtifactMultiplier = 1;
        foreach (var artifactSlot in contractFarmArtifactSlots)
        {
            if (artifactSlot.Occupied)
            {
                var artifact = artifactInventory.FirstOrDefault(a => a.ItemId == artifactSlot.ItemId);
                if (artifact.Artifact.Spec.Name == ArtifactSpec.Types.Name.OrnateGusset)
                {
                    var artifactMultiplier = artifactMultipliers[artifact.Artifact.Spec.Name][(int)artifact.Artifact.Spec.Level][(int)artifact.Artifact.Spec.Rarity];

                    if (farm.EggType == Ei.Egg.Enlightenment)
                    {
                        double clarityStoneMultiplier = 0;
                        foreach (var stone in artifact.Artifact.Stones)
                        {
                            if (stone.Name == ArtifactSpec.Types.Name.ClarityStone)
                            {
                                clarityStoneMultiplier += artifactMultipliers[stone.Name][(int)stone.Level][0];
                            }
                        }

                        artifactMultiplier *= clarityStoneMultiplier;
                    }

                    habCapacityArtifactMultiplier *= 1 + artifactMultiplier;
                }
            }
        }

        Dictionary<string, double> habCapacityResearchMultipliersPerLevel = new Dictionary<string, double>
        {
            { "hab_capacity1", 0.05 },
            { "microlux", 0.05 },
            { "grav_plating", 0.02 },
            { "wormhole_dampening", 0.02 },
        };

        double habCapacityResearchMultiplier = 1;
        foreach (var commonResearch in farm.CommonResearch)
        {
            if (habCapacityResearchMultipliersPerLevel.ContainsKey(commonResearch.Id))
            {
                habCapacityResearchMultiplier *= 1 + habCapacityResearchMultipliersPerLevel[commonResearch.Id] * commonResearch.Level;
            }
        }

        return Math.Round(habCapacityArtifactMultiplier * habCapacityResearchMultiplier * 4 * 6e8); //Assumes 4 habs and all Chicken Universes
    }

    private static async Task<string> PostRequest(string url, FormUrlEncodedContent json)
    {
        using (var client = new HttpClient())
        {
            var response = await client.PostAsync(url, json);
            return await response.Content.ReadAsStringAsync();
        }
    }

    private static Dictionary<int, string> roles = new Dictionary<int, string>
    {
        { 0, "Farmer" },
        { 1, "Farmer II" },
        { 2, "Farmer III" },
        { 3, "Kilofarmer" },
        { 4, "Kilofarmer II" },
        { 5, "Kilofarmer III" },
        { 6, "Megafarmer" },
        { 7, "Megafarmer II" },
        { 8, "Megafarmer III" },
        { 9, "Gigafarmer" },
        { 10, "Gigafarmer II" },
        { 11, "Gigafarmer III" },
        { 12, "Terafarmer" },
        { 13, "Terafarmer II" },
        { 14, "Terafarmer III" },
        { 15, "Petafarmer" },
        { 16, "Petafarmer II" },
        { 17, "Petafarmer III" },
        { 18, "Exafarmer" },
        { 19, "Exafarmer II" },
        { 20, "Exafarmer III" },
        { 21, "Zettafarmer" },
        { 22, "Zettafarmer II" },
        { 23, "Zettafarmer III" },
        { 24, "Yottafarmer" },
        { 25, "Yottafarmer II" },
        { 26, "Yottafarmer III" },
        { 27, "Xennafarmer" },
        { 28, "Xennafarmer II" },
        { 29, "Xennafarmer III" },
        { 30, "Weccafarmer" },
        { 31, "Weccafarmer II" },
        { 32, "Weccafarmer III" },
        { 33, "Vendafarmer" },
        { 34, "Vendafarmer II" },
        { 35, "Vendafarmer III" },
        { 36, "Uadafarmer" },
        { 37, "Uadafarmer II" },
        { 38, "Uadafarmer III" },
        { 39, "Treidafarmer" },
        { 40, "Treidafarmer II" },
        { 41, "Treidafarmer III" },
        { 42, "Quadafarmer" },
        { 43, "Quadafarmer II" },
        { 44, "Quadafarmer III" },
        { 45, "Pendafarmer" },
        { 46, "Pendafarmer II" },
        { 47, "Pendafarmer III" },
        { 48, "Exedafarmer" },
        { 49, "Exedafarmer II" },
        { 50, "Exedafarmer III" },
        { 51, "Infinifarmer" },
    };

    public static string SoulPowerToFarmerRole(double soulPower)
    {
        return roles[(int)Math.Floor(soulPower)];
    }
}