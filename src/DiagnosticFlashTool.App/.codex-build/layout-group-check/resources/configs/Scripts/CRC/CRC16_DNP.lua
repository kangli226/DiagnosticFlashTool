function Main(data)
  local crc = 0x0000
  for i = 1, #data do
    crc = crc ~ string.byte(data, i)
    for _ = 1, 8 do
      if (crc & 0x0001) ~= 0 then
        crc = ((crc >> 1) ~ 0xA6BC) & 0xFFFF
      else
        crc = (crc >> 1) & 0xFFFF
      end
    end
  end
  return (~crc) & 0xFFFF
end
