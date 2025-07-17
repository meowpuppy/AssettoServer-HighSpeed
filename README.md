# AssettoServer-HighSpeed

[![License](https://img.shields.io/github/license/HighSpeedgg/AssettoServer-HighSpeed)](LICENSE)
[![Discord](https://img.shields.io/discord/960380637628498002?label=HighSpeed%20Discord)](https://discord.gg/highspeed)

## Current Version

**Version: 0.0.54**

> ⚠️ **Note:** This is **not** the newest version of AssettoServer or its forks. This repository is currently on version 0.0.54, which may lack the latest features, fixes, and improvements found in newer releases.  
> For the absolute latest developments, **check [compujuckel/AssettoServer](https://github.com/compujuckel/AssettoServer)** or other related forks.

## Overview

**AssettoServer-HighSpeed** is a custom fork of [AssettoServer](https://github.com/compujuckel/AssettoServer) built specifically for the [HighSpeed.gg](https://highspeed.gg) freeroam and "cut up" server. This project takes the original AssettoServer and transforms it into the ultimate playground for cutting up in traffic—enabling players to weave, blast, and cruise through realistic, dynamic traffic in Assetto Corsa.

## Key Features

- 🚗 **Cut Up Traffic Handling**: Advanced, custom traffic logic for weaving and cutting up in dense traffic, designed for the full "cut up" freeroam experience.
- 🌎 **Freeroam Oriented**: Persistent, open-world environments where you can explore, cruise, and challenge yourself in traffic, not just race.
- 🛠️ **Custom Server Logic**: Purpose-built features and backend improvements focused on smooth, stable, and responsive multiplayer sessions.
- 🔧 **Constantly Evolving**: Actively maintained and updated for the HighSpeed.gg community, with new traffic behaviors, server tweaks, and gameplay enhancements.


# New Traffic Config Values

In this custom fork, we have introduced new configuration values to enhance the traffic experience. These values allow for more precise control over how traffic behaves in the game, making it easier to create a dynamic and engaging environment for players.

They are placed in the `extra_config.yml` file, which is loaded by the server at startup. You can modify these values to suit your server's needs.

## New Traffic Config Values

```yaml
AiParams:
    
    ... # Existing AI parameters remain unchanged

    # Minimum lane change speed in seconds
    MinLaneChangeTime: 4
    # Maximum lane change speed in seconds
    MaxLaneChangeTime: 7
    # Minimum lane change cooldown in seconds
    MinLaneChangeCooldown: 30
    # Maximum lane change cooldown in seconds
    MaxLaneChangeCooldown: 120
    # The required amount of flashes to trigger a lane change
    RequiredFlashes: 3
    # The amount of time in seconds you must flash your lights to trigger a lane change
    FlashWindow: 5
```

## About HighSpeed.gg

[HighSpeed.gg](https://highspeed.gg) is a community-driven Assetto Corsa server that specializes in high-energy freeroam and cut up sessions. The server uses this special build to deliver the most authentic and enjoyable cut up traffic experience available in Assetto Corsa.

## What does "Cut Up" mean?

"Cut up" refers to the driving style of weaving and maneuvering through traffic at speed, threading the needle between AI and real drivers, much like viral street runs seen online. This server is purpose-built for that style—expect challenging, dynamic traffic and wide-open roads.

## Credits

- Base server by [compujuckel/AssettoServer](https://github.com/compujuckel/AssettoServer)
- Custom modifications and ongoing development by the [HighSpeed.gg](https://highspeed.gg) team

## License

This project inherits the license of the original AssettoServer. See [LICENSE](LICENSE) for details.
