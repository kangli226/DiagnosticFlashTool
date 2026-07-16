local function byte_at(seed, index)
    if type(seed) == "string" then
        return string.byte(seed, index) or 0
    end
    return seed[index] or 0
end

local function u32(value)
    return value & 0xFFFFFFFF
end

local function u8(value)
    return value & 0xFF
end

function Main(seed, level)
    if not seed or #seed < 4 then
        error("Seed array must have at least 4 bytes")
    end

    local delta = 0x9E3779B9
    local sum = 0
    local SA_constant = {0x5A, 0x4C, 0x57, 0x4C}
    local securityLevel = math.floor((level + 1) / 2)

    for i = 1, securityLevel do
        for j = 1, 4 do
            SA_constant[j] = u8((SA_constant[j] << 5) | (SA_constant[j] >> 3))
        end
    end

    local k = {SA_constant[1], SA_constant[2], SA_constant[3], SA_constant[4]}

    local V0 = u32((byte_at(seed, 1) << 24) |
                   (byte_at(seed, 2) << 16) |
                   (byte_at(seed, 3) << 8) |
                   byte_at(seed, 4))

    local V1 = u32(((~byte_at(seed, 1) & 0xFF) << 24) |
                   ((~byte_at(seed, 2) & 0xFF) << 16) |
                   ((~byte_at(seed, 3) & 0xFF) << 8) |
                   (~byte_at(seed, 4) & 0xFF))

    for j = 1, 2 do
        local v0_part1 = u32(((V1 << 4) ~ (V1 >> 5)) + V1)
        local v0_part2 = u32(sum + k[(sum & 3) + 1])
        V0 = u32(V0 + (v0_part1 ~ v0_part2))

        sum = u32(sum + delta)

        local v1_part1 = u32(((V0 << 4) ~ (V0 >> 5)) + V0)
        local v1_part2 = u32(sum + k[((sum >> 11) & 3) + 1])
        V1 = u32(V1 + (v1_part1 ~ v1_part2))
    end

    return string.char((V0 >> 24) & 0xFF,
                       (V0 >> 16) & 0xFF,
                       (V0 >> 8) & 0xFF,
                       V0 & 0xFF)
end