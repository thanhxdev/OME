import struct
import time

def read_exactly(pipe, size):
    buf = b''
    while len(buf) < size:
        chunk = pipe.read(size - len(buf))
        if not chunk:
            raise EOFError()
        buf += chunk
    return buf

def write_cmd(pipe, cmd_type, payload):
    magic = 0x4F4D4549
    version = 1
    seq = 1
    timestamp = 0
    client_id = 0
    header = struct.pack('<I H H I I I Q I', magic, version, 0, cmd_type, seq, len(payload), timestamp, client_id)
    pipe.write(header + payload)
    pipe.flush()

def read_response(pipe):
    header = read_exactly(pipe, 32)
    magic, version, pad, status, seq, size, err, timestamp = struct.unpack('<I H H I I I I Q', header)
    if status != 0:
        raise Exception(f"Status != 0 (status={status}, err={err})")
    if size > 0:
        payload = read_exactly(pipe, size)
    else:
        payload = b''
    return payload

pipe = open(r'\\.\pipe\OpenMediaSDK', 'r+b', 0)

# 1. Create pipeline
payload = struct.pack('<I', 8) + b'Player_1' + struct.pack('<I I d', 1920, 1080, 60.0)
write_cmd(pipe, 0x0100, payload)
resp = read_response(pipe)
pipeline_id = struct.unpack('<I', resp[:4])[0]
print(f"Pipeline ID: {pipeline_id}")

# 2. Open source
url = 'file:C:/Users/ASUS NUC/Desktop/Code/OME/tests/data/sample.mp4'.encode('utf-8')
payload2 = struct.pack('<I I I', pipeline_id, 1, len(url)) + url + struct.pack('<B I', 0, 0)
write_cmd(pipe, 0x0200, payload2)
read_response(pipe)
print("Opened source")

# 3. GetSourceInfo
payload3 = struct.pack('<I I', pipeline_id, 1)
write_cmd(pipe, 0x0203, payload3)
resp3 = read_response(pipe)
print(f"GetSourceInfo resp size: {len(resp3)}")

strlen = struct.unpack('<I', resp3[:4])[0]
offset = 4
url_resp = resp3[offset:offset+strlen].decode('utf-8')
offset += strlen
duration, w, h, fps = struct.unpack('<d I I d', resp3[offset:offset+24])

print(f"URL: {url_resp}, Duration: {duration}, Width: {w}, Height: {h}, FPS: {fps}")
