local carsBySessionId = {}
local debugInfoBySessionId = {}

local debugInfoEnabled = false

for i = 0, sim.carsCount - 1 do
    local c = ac.getCar(i)
    carsBySessionId[c.sessionID] = c
    debugInfoBySessionId[c.sessionID] = {
        CurrentSpeed = -1,
        TargetSpeed = -1,
        MaxSpeed = -1,
        ClosestAiObstacle = -1,
    }
end

local packetLen = 20
local debugEvent = ac.OnlineEvent({
    ac.StructItem.key("ai_debug"),
    SessionIds = ac.StructItem.array(ac.StructItem.byte(), packetLen),
    CurrentSpeeds = ac.StructItem.array(ac.StructItem.byte(), packetLen),
    TargetSpeeds = ac.StructItem.array(ac.StructItem.byte(), packetLen),
    MaxSpeeds = ac.StructItem.array(ac.StructItem.byte(), packetLen),
    ClosestAiObstacles = ac.StructItem.array(ac.StructItem.int16(), packetLen),
}, function (sender, message)
    for i = 0, packetLen - 1 do
        local sessionId = message.SessionIds[i]
        if sessionId ~= 255 then
            debugInfoBySessionId[sessionId].CurrentSpeed = message.CurrentSpeeds[i]
            debugInfoBySessionId[sessionId].TargetSpeed = message.TargetSpeeds[i]
            debugInfoBySessionId[sessionId].MaxSpeed = message.MaxSpeeds[i]
            debugInfoBySessionId[sessionId].ClosestAiObstacle = message.ClosestAiObstacles[i]
        end
    end
end)

function script.draw3D()
    if not debugInfoEnabled then
        return
    end
    for sessionID, debugInfo in pairs(debugInfoBySessionId) do
        local car = carsBySessionId[sessionID]
        if car.position:closerToThan(ac.getCameraPosition(), 200) then
            render.debugArrow(car.position, car.position + car.look, rgbm.colors.white, 1.5)
            
            if car.ping == 0 then
                -- get the cross of the car's look vector and the up vector without using car.look:cross()
                local up = vec3(0, 1, 0)
                local right = vec3(
                    car.look.y * up.z - car.look.z * up.y,
                    car.look.z * up.x - car.look.x * up.z,
                    car.look.x * up.y - car.look.y * up.x
                )
                local upCross = vec3(
                    right.y * car.look.z - right.z * car.look.y,
                    right.z * car.look.x - right.x * car.look.z,
                    right.x * car.look.y - right.y * car.look.x
                )

                -- draw a box around the car using render.debugLine
                local size = 0.5
                local p1 = car.position + right * 2.75 * size - upCross * size + vec3(car.look.x, car.look.y + 0.15, car.look.z) * 2.75
                local p2 = car.position - right * 2.75 * size - upCross * size + vec3(car.look.x, car.look.y + 0.15, car.look.z) * 2.75
                local p3 = car.position - right * 2.75 * size + upCross / size + vec3(car.look.x, car.look.y - 0.15, car.look.z) * 2.75
                local p4 = car.position + right * 2.75 * size + upCross / size + vec3(car.look.x, car.look.y - 0.15, car.look.z) * 2.75
                local p5 = car.position + right * 2.75 * size + upCross * -size - vec3(car.look.x, car.look.y - 0.15, car.look.z) * 2.75
                local p6 = car.position - right * 2.75 * size + upCross * -size - vec3(car.look.x, car.look.y - 0.15, car.look.z) * 2.75
                local p7 = car.position - right * 2.75 * size - upCross / -size - vec3(car.look.x, car.look.y + 0.15, car.look.z) * 2.75
                local p8 = car.position + right * 2.75 * size - upCross / -size - vec3(car.look.x, car.look.y + 0.15, car.look.z) * 2.75

                -- render.debugPoint(p1, 2, rgbm.colors.red)
                -- render.debugPoint(p2, 2, rgbm.colors.green)
                -- render.debugPoint(p3, 2, rgbm.colors.blue)
                -- render.debugPoint(p4, 2, rgbm.colors.yellow)

                -- render.debugPoint(p5, 2, rgbm.colors.red)
                -- render.debugPoint(p6, 2, rgbm.colors.green)
                -- render.debugPoint(p7, 2, rgbm.colors.blue)
                -- render.debugPoint(p8, 2, rgbm.colors.yellow)

                local boxColor = rgbm.colors.red

                render.debugLine(p1, p2, boxColor)
                render.debugLine(p2, p3, boxColor)
                render.debugLine(p3, p4, boxColor)
                render.debugLine(p4, p1, boxColor)
                render.debugLine(p5, p6, boxColor)
                render.debugLine(p6, p7, boxColor)
                render.debugLine(p7, p8, boxColor)
                render.debugLine(p8, p5, boxColor)
                render.debugLine(p1, p5, boxColor)
                render.debugLine(p2, p6, boxColor)
                render.debugLine(p3, p7, boxColor)
                render.debugLine(p4, p8, boxColor)

                -- do the same but draw a box that extends 15 m from the rear of the car
                local rearBoxSize = 0.5
                local rearBoxLength = 0
                local rearBoxColor = rgbm.colors.blue

                -- Rear box starts at the back of the car and extends 15m backwards
                rearBoxLength = 15
                -- Calculate rear direction (opposite of car.look)
                local rearDir = -car.look
                -- Car rear center position (assuming car.position is center, move half car length back)
                local carHalfLength = 5.50 * size
                local rearStart = car.position - car.look * carHalfLength

                -- Corners at rear of car
                local rearBoxP1 = rearStart + right * 2.75 * rearBoxSize - upCross * rearBoxSize + up * 0.40
                local rearBoxP2 = rearStart - right * 2.75 * rearBoxSize - upCross * rearBoxSize + up * 0.40
                local rearBoxP3 = rearStart - right * 2.75 * rearBoxSize + upCross * rearBoxSize - up * -1.1
                local rearBoxP4 = rearStart + right * 2.75 * rearBoxSize + upCross * rearBoxSize - up * -1.1

                -- Corners at the end of the rear box (15m back)
                local rearEnd = rearStart + rearDir * rearBoxLength
                local rearBoxP5 = rearEnd + right * 2.75 * rearBoxSize - upCross * rearBoxSize + up * 0.40
                local rearBoxP6 = rearEnd - right * 2.75 * rearBoxSize - upCross * rearBoxSize + up * 0.40
                local rearBoxP7 = rearEnd - right * 2.75 * rearBoxSize + upCross * rearBoxSize - up * -1.1
                local rearBoxP8 = rearEnd + right * 2.75 * rearBoxSize + upCross * rearBoxSize - up * -1.1

                render.debugLine(rearBoxP1, rearBoxP2, rearBoxColor)
                render.debugLine(rearBoxP2, rearBoxP3, rearBoxColor)
                render.debugLine(rearBoxP3, rearBoxP4, rearBoxColor)
                render.debugLine(rearBoxP4, rearBoxP1, rearBoxColor)
                render.debugLine(rearBoxP5, rearBoxP6, rearBoxColor)
                render.debugLine(rearBoxP6, rearBoxP7, rearBoxColor)
                render.debugLine(rearBoxP7, rearBoxP8, rearBoxColor)
                render.debugLine(rearBoxP8, rearBoxP5, rearBoxColor)
                render.debugLine(rearBoxP1, rearBoxP5, rearBoxColor)
                render.debugLine(rearBoxP2, rearBoxP6, rearBoxColor)
                render.debugLine(rearBoxP3, rearBoxP7, rearBoxColor)
                render.debugLine(rearBoxP4, rearBoxP8, rearBoxColor)

                -- draw a line to all the cars within 10 meters of each car
                for j = 0, sim.carsCount - 1 do
                    local otherCar = ac.getCar(j)
                    if otherCar.sessionID ~= sessionID and otherCar.position:closerToThan(car.position, 10) then
                        render.debugLine(car.position, otherCar.position, rgbm.colors.green)
                        render.debugText(otherCar.position + vec3(0, 0.5, 0), tostring(otherCar.sessionID), rgbm.colors.green)
                    end
                end
            end
        end
    end
end

local UIToggle = debugInfoEnabled

ui.registerOnlineExtra(ui.Icons.Settings, "AI Debug",
    function() return true end,
    function()
        if ui.checkbox('Enable AI Debug', UIToggle) then
            UIToggle = not UIToggle
            debugInfoEnabled = UIToggle
        end
    end,
    function() end,
    ui.OnlineExtraFlags.Tool)
