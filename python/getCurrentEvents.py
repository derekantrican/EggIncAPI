import requests
import ei_pb2
import base64

user_id = 'EI1234567890123456'

periodicals_request = ei_pb2.GetPeriodicalsRequest()
periodicals_request.user_id = user_id
periodicals_request.current_client_version = 42

url = 'https://www.auxbrain.com/ei/get_periodicals' # This endpoint can also be used for things like current sales & contract status - refernce the PeriodicalsResponse definition in ei.proto
data = { 'data' : base64.b64encode(periodicals_request.SerializeToString()).decode('utf-8') }
response = requests.post(url, data = data)

authenticated_message = ei_pb2.AuthenticatedMessage()
authenticated_message.ParseFromString(base64.b64decode(response.text))

periodicals_response = ei_pb2.PeriodicalsResponse()
periodicals_response.ParseFromString(authenticated_message.message)

for event in periodicals_response.events.events:
    print("event details: ")
    print("  type: " + event.type)
    print("  text: " + event.subtitle)
    print("  multiplier: " + str(event.multiplier))