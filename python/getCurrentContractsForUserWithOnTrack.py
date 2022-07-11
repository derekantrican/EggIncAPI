import requests
import ei_pb2
import base64

user_id = 'EI1234567890123456'

first_contact_request = ei_pb2.EggIncFirstContactRequest()
first_contact_request.ei_user_id = user_id
first_contact_request.client_version = 36

url = 'https://wasmegg.zw.workers.dev/?url=https://www.auxbrain.com/ei/bot_first_contact'
data = { 'data' : base64.b64encode(first_contact_request.SerializeToString()).decode('utf-8') }
response = requests.post(url, data = data)

first_contact_response = ei_pb2.EggIncFirstContactResponse()
first_contact_response.ParseFromString(base64.b64decode(response.text))

for contract in first_contact_response.backup.contracts.contracts:
    print("current coop details: ")
    print("  contract_id: " + contract.contract.identifier)
    print("  coop_id: " + contract.coop_identifier)

    if contract.contract.coop_allowed:
        # Make another request to the API to get the coop status
        coop_status_request = ei_pb2.ContractCoopStatusRequest()
        coop_status_request.contract_identifier = contract.contract.identifier
        coop_status_request.coop_identifier = contract.coop_identifier
        coop_status_request.user_id = user_id

        url = 'https://wasmegg.zw.workers.dev/?url=https://www.auxbrain.com/ei/coop_status'
        data = { 'data' : base64.b64encode(coop_status_request.SerializeToString()).decode('utf-8') }
        response = requests.post(url, data = data)

        authenticated_message = ei_pb2.AuthenticatedMessage()
        authenticated_message.ParseFromString(base64.b64decode(response.text))

        coop_status_response = ei_pb2.ContractCoopStatusResponse()
        coop_status_response.ParseFromString(authenticated_message.message)

        total_eggs_per_hour = sum((contributor.contribution_rate * 3600) for contributor in coop_status_response.contributors)
        target_amount = contract.contract.goals[-1].target_amount
        required_eggs_per_hour = ((target_amount - coop_status_response.total_amount) / coop_status_response.seconds_remaining) * 3600

        if total_eggs_per_hour >= required_eggs_per_hour:
            print('  contract is on track!')
    else:
        # For a solo contract, we cannot simply get the "contribution_rate" of the player (because we don't have coop_status_response).
        # So we have to calculate manually using researches, artifacts, etc

        farm_index = -1
        contract_farm = None
        for i, farm in enumerate(first_contact_response.backup.farms):
            if farm.contract_id == contract.contract.identifier:
                contract_farm = farm
                farm_index = i
                break

        ### ARTIFACTS
        artifact_multipliers = { # Map taken from https://wasmegg.netlify.app/artifact-explorer/
            ei_pb2.ArtifactSpec.QUANTUM_METRONOME: [
                [0.05], # Misaligned
                [0.1, 0.12], # Adequate
                [0.15, 0.17, 0.2], # Perfect
                [0.25, 0.27, 0.3, 0.35], # Reggference
            ],
            ei_pb2.ArtifactSpec.INTERSTELLAR_COMPASS: [
                [0.05], # Miscalibrated
                [0.1], # Regular
                [0.2, 0.22], # Precise
                [0.3, 0.35, 0.4, 0.5], # Clairvoyant
            ],
            ei_pb2.ArtifactSpec.TACHYON_STONE: [
                # Fragment cannot be set (no level)
                0.02, # Regular
                0.04, # Eggsquisite
                0.05, # Brilliant
            ],
            ei_pb2.ArtifactSpec.QUANTUM_STONE: [
                # Fragment cannot be set (no level)
                0.02, # Regular
                0.04, # Phased
                0.05, # Meggnificient
            ],
        }

        artifact_inventory = first_contact_response.backup.artifacts_db.inventory_items
        contract_farm_artifact_slots = first_contact_response.backup.artifacts_db.active_artifact_sets[farm_index].slots
        
        egg_laying_rate_artifact_multiplier = 1
        shipping_rate_artifact_multiplier = 1
        for artifact_slot in contract_farm_artifact_slots:
            if artifact_slot.occupied:
                artifact = [artifact for artifact in artifact_inventory if artifact.item_id == artifact_slot.item_id][0]

                if artifact.artifact.spec.name == ei_pb2.ArtifactSpec.QUANTUM_METRONOME:
                    artifact_multiplier = artifact_multipliers[artifact.artifact.spec.name][artifact.artifact.spec.level][artifact.artifact.spec.rarity]
                    egg_laying_rate_artifact_multiplier *= 1 + artifact_multiplier
                elif artifact.artifact.spec.name == ei_pb2.ArtifactSpec.INTERSTELLAR_COMPASS:
                    artifact_multiplier = artifact_multipliers[artifact.artifact.spec.name][artifact.artifact.spec.level][artifact.artifact.spec.rarity]
                    shipping_rate_artifact_multiplier *= 1 + artifact_multiplier
                
                for stone in artifact.artifact.stones:
                    if stone.name == ei_pb2.ArtifactSpec.TACHYON_STONE:
                        stone_multiplier = artifact_multipliers[stone.name][stone.level]
                        egg_laying_rate_artifact_multiplier *= 1 + stone_multiplier
                    elif stone.name == ei_pb2.ArtifactSpec.QUANTUM_STONE:
                        stone_multiplier = artifact_multipliers[stone.name][stone.level]
                        shipping_rate_artifact_multiplier *= 1 + stone_multiplier
        
        ### EGG LAYING RATE
        egg_laying_research_multipliers_per_level = {
            "comfy_nests" : 0.1,
            "hen_house_ac" : 0.05,
            "improved_genetics" : 0.15,
            "time_compress" : 0.1,
            "timeline_diversion" : 0.02,
            "relativity_optimization" : 0.1,
            "epic_egg_laying" : 0.05,
        }

        egg_laying_research_multiplier = 1
        for common_research in contract_farm.common_research:
            if common_research.id in egg_laying_research_multipliers_per_level:
                egg_laying_research_multiplier *= 1 + egg_laying_research_multipliers_per_level[common_research.id] * common_research.level
        
        epic_comfy_nests_level = [epic_research for epic_research in first_contact_response.backup.game.epic_research if epic_research.id == "epic_egg_laying"][0].level
        egg_laying_research_multiplier *= 1 + egg_laying_research_multipliers_per_level["epic_egg_laying"] * epic_comfy_nests_level

        layable_eggs_per_second = contract_farm.num_chickens * 1/30 * egg_laying_research_multiplier * egg_laying_rate_artifact_multiplier
        
        ### SHIPPING CAPACITY
        vehicle_capacities = []
        for i, vehicle_id in enumerate(contract_farm.vehicles):
            if vehicle_id != 11: # Calculation is based off all vehicles being hyperloops
                print("  found vehicle that is not a hyperloop. These calculations are all based on hyperloops so these calculations will not be accurate...")
            else:
                vehicle_capacities.append((50e6 / 60) * contract_farm.train_length[i])

        shipping_rate_research_multipliers_per_level = {
            "leafsprings" : 0.05,
            "lightweight_boxes" : 0.1,
            "driver_training" : 0.05,
            "super_alloy" : 0.05,
            "quantum_storage" : 0.05,
            "hover_upgrades" : 0.05, # Only valid for hover vehicles (but our calculations assume that all vehicles are hyperloops)
            "dark_containment" : 0.05,
            "neural_net_refine" : 0.05,
            "hyper_portalling" : 0.05, # Only valid for hyperloops (but our calculations assume that all vehicles are hyperloops)
            "transportation_lobbyist" : 0.05,
        }

        shipping_rate_research_multiplier = 1
        for common_research in contract_farm.common_research:
            if common_research.id in shipping_rate_research_multipliers_per_level:
                shipping_rate_research_multiplier *= 1 + shipping_rate_research_multipliers_per_level[common_research.id] * common_research.level

        transportation_lobbyist_level = [epic_research for epic_research in first_contact_response.backup.game.epic_research if epic_research.id == "transportation_lobbyist"][0].level
        shipping_rate_research_multiplier *= 1 + shipping_rate_research_multipliers_per_level["transportation_lobbyist"] * transportation_lobbyist_level

        shippable_eggs_per_second = sum(map(lambda vehicle_capacity: vehicle_capacity * shipping_rate_research_multiplier * shipping_rate_artifact_multiplier, vehicle_capacities))

        ### FINAL CALCULATION
        target_amount = contract.contract.goals[-1].target_amount
        contract_end_time = contract.time_accepted + contract.contract.length_seconds
        seconds_remaining = contract_end_time - time.time()

        required_eggs_per_hour = ((target_amount - contract_farm.eggs_laid) / seconds_remaining) * 3600
        eggs_per_hour = min(layable_eggs_per_second, shippable_eggs_per_second) * 3600

        if eggs_per_hour >= required_eggs_per_hour:
            print('  contract is on track!')