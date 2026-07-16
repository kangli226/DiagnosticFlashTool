function Main(data)
  local crc = 0xFFFFFFFF
  for i = 1, #data do
    crc = crc ~ string.byte(data, i)
    for _ = 1, 8 do
      if (crc & 0x00000001) ~= 0 then
        crc = ((crc >> 1) ~ 0xEDB88320) & 0xFFFFFFFF
      else
        crc = (crc >> 1) & 0xFFFFFFFF
      end
    end
  end
  return (~crc) & 0xFFFFFFFF
end
