import requests
import ei_pb2
import base64
from google.protobuf.json_format import MessageToJson

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
