# Soulcatcher Crossbow Fix

⚠️ **TEMPORARY PATCH MOD** ⚠️  
This is a **client-side patch** for **Soulcatcher JC KG Additions** that fixes broken crossbow-related gem effects.

The patch will be **removed immediately** once the original mod author fixes the issue upstream.

---

## What does this fix?

In the current version of **Soulcatcher JC KG Additions**, the following gem effects for crossbows are defined but not applied correctly:

- **Hare Soul Power** – damage bonus ❌
- **Dverger Soul Power** – reload speed bonus ❌

This patch restores the intended behavior:

- 🐇 **Hare Soul Power** → **increases crossbow damage**
- 🧙 **Dverger Soul Power** → **reduces crossbow reload time**

✅ Works with vanilla and modded crossbows  
✅ Supports multiple crossbows (skill-based + prefab name fallback)  
✅ Fully tested in real gameplay  
⚠️ **Must be installed on every client** (this is a client-side fix)

---

## Requirements

This mod is a **PATCH**, not a standalone mod.

### Required mods:
- **Soulcatcher JC KG Additions**  
  https://thunderstore.io/c/valheim/p/KGvalheim/Soulcatcher_JC_KG_Additions/
- **Jewelcrafting**
- **BepInExPack Valheim**

---

## Installation

1. Install all required dependencies.
2. Install **Soulcatcher Crossbow Fix**.
3. Make sure **EVERY CLIENT** in multiplayer has this mod installed.
4. Done.

---

## FAQ

### Q: Does this work on dedicated servers?
Yes, **but it must be installed on all clients**.  
This patch intentionally runs client-side to ensure consistent behavior.

### Q: Will this conflict with future updates?
Possibly.  
This patch is meant to be **temporary** and will be removed once the upstream mod is fixed.

### Q: Why not patch server-side?
Because reload timing and attack flow for crossbows are partially client-driven in Valheim.  
This approach proved to be the **most stable and reliable**.

---

## Credits

- **Original mod:**  
  Soulcatcher JC KG Additions by **KGvalheim**  
  https://thunderstore.io/c/valheim/p/KGvalheim/Soulcatcher_JC_KG_Additions/

Source / GitHub:
https://github.com/Gerbesh/Soulcatcher_crossbow_fix

This patch does **NOT** include any original assets or code from the original mod.

---

## Support & Community

<p align="center"></p>
<h2>For questions, feedback or bug reports find Gerbesh in the Odin Plus Team on Discord:</h2>
<p></p>
<p align="center">
<a href="https://discord.gg/mbkPcvu9ax">
 <img src="https://noobtrap.eu/images/crystallights/oplusdisc.png">
</a>
</p>

If you encounter bugs, have feature requests or need help:
- Write directly on Discord
- I respond quickly and actively maintain this mod

---

## License

MIT License (see LICENSE file)
