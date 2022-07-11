var coopStatus = await EggIncApi.EggIncApi.GetCoopStatus("shipping-surge", "shippingrun", "EI1234567890123456");

// await GetCurrentContractsForUserWithOnTrack("EI1234567890123456");

async Task GetCurrentContractsForUserWithOnTrack(string userId)
{
    var firstContact = await EggIncApi.EggIncApi.GetFirstContact(userId);
    foreach (var contract in firstContact.Backup.Contracts.Contracts)
    {
        Console.WriteLine("current coop details: ");
        Console.WriteLine($"  contract_id: {contract.Contract.Identifier}");
        Console.WriteLine($"  coop_id: {contract.CoopIdentifier}");

        if (contract.Contract.CoopAllowed)
        {
            if (await EggIncApi.EggIncApi.IsCoopContractOnTrack(contract))
            {
                Console.WriteLine("  contract is on track!");
            }
        }
        else
        {
            if (EggIncApi.EggIncApi.IsSoloContractOnTrack(contract, firstContact.Backup))
            {
                Console.WriteLine("  contract is on track!");
            }
        }
    }
}