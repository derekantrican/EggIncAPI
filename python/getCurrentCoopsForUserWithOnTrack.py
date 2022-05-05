import requests
import ei_pb2
import base64

user_id = 'EI1234567890123456'

first_contact_request = ei_pb2.EggIncFirstContactRequest()
first_contact_request.ei_user_id = user_id
first_contact_request.client_version = 36

url = 'https://wasmegg.zw.workers.dev/?url=https://www.auxbrain.com/ei/first_contact'
data = { 'data' : base64.b64encode(first_contact_request.SerializeToString()).decode('utf-8') }
response = requests.post(url, data = data)

authenticated_message = ei_pb2.AuthenticatedMessage()
authenticated_message.ParseFromString(base64.b64decode(response.text))

first_contact_response = ei_pb2.EggIncFirstContactResponse()
first_contact_response.ParseFromString(authenticated_message.message)

for contract in first_contact_response.backup.contracts.contracts:
    print("current coop details: ")
    print("  contract_id: " + contract.contract.identifier)
    print("  coop_id: " + contract.coop_identifier)

    # Make another request to the API to get the coop status
    coop_status_request = ei_pb2.ContractCoopStatusRequest()
    coop_status_request.contract_identifier =  contract.contract.identifier
    coop_status_request.coop_identifier = contract.coop_identifier

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
    print("  on track? " + str(total_eggs_per_hour >= required_eggs_per_hour))
