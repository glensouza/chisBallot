# Branding

chisBallot follows Citrus Hills Intermediate School's identity so the site
and any printed material feel like they belong to the school.

| | |
|---|---|
| School | Citrus Hills Intermediate School, Corona-Norco USD |
| Mascot | Jaguars |
| Colors | Blue and gray |

## Palette

| Token | Hex | Use |
|---|---|---|
| `--chis-blue` | `#1B4F9C` | Headers, primary buttons, links |
| `--chis-blue-light` | `#2F6FC4` | Hover states, large numbers |
| `--chis-gray` | `#6B7785` | Secondary text, icons |
| `--chis-gray-dark` | `#4A5563` | Emphasis text |
| `--chis-silver` | `#C9D3E0` | Borders, text on blue |
| `--chis-tint` | `#EEF2F7` | Card and panel backgrounds |
| `--chis-ink` | `#1B1F24` | Body text |

The blue is an approximation. Replace `--chis-blue` with the exact value from
the school's logo file once the school supplies it; every other use derives
from the tokens in `brand.css`.

## Rules

- Body text is `--chis-ink` on white. White text only goes on `--chis-blue`.
- One primary action per screen, in `--chis-blue`.
- The jaguar artwork belongs to the school. It is in the site header (a white
  chip on the blue bar, `logo.png` in each site's `wwwroot`). Keep the principal's
  written permission on file, and do not add the paw or other school artwork
  without it.
- Slides use the same palette (see the proposal deck).

## Installing on a phone or computer

The voting site has a web app manifest (`manifest.webmanifest`) and icons
(192 and 512 pixels, a maskable 512, and an Apple touch icon), so a member can
add it to a home screen or install it from the browser. It is only a shortcut
that opens the same site. There is deliberately **no service worker** and no
offline mode: the site must always load the current code from the server, and a
vote cannot be cast offline anyway. The admin site is not installable.
