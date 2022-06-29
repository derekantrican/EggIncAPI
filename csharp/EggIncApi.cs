using Ei;
using Google.Protobuf;
using static Ei.Backup.Types;

namespace EggIncApi;
public class EggIncApi
{
    private const int CLIENTVERSION = 40;
    public static async Task<ContractCoopStatusResponse> GetCoopStatus(string contractId, string coopId)
    {
        ContractCoopStatusRequest coopStatusRequest = new ContractCoopStatusRequest();
        coopStatusRequest.ContractIdentifier = contractId;
        coopStatusRequest.CoopIdentifier = coopId;

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
        var coopStatusResponse = await EggIncApi.GetCoopStatus(contract.Contract.Identifier, contract.CoopIdentifier);
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
                        var stoneMultiplier = artifactMultipliers[artifact.Artifact.Spec.Name][(int)artifact.Artifact.Spec.Level][(int)artifact.Artifact.Spec.Rarity];
                        eggLayingRateArtifactMultiplier *= 1 + stoneMultiplier;
                    }
                    else if (stone.Name == ArtifactSpec.Types.Name.QuantumStone)
                    {
                        var stoneMultiplier = artifactMultipliers[artifact.Artifact.Spec.Name][(int)artifact.Artifact.Spec.Level][(int)artifact.Artifact.Spec.Rarity];
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

    private static async Task<string> PostRequest(string url, FormUrlEncodedContent json)
    {
        using (var client = new HttpClient())
        {
            var response = await client.PostAsync(url, json);
            return await response.Content.ReadAsStringAsync();
        }
    }
}