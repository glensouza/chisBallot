# The audit page

Every election has a public audit page at
`vote.chisballot.org/audit/{slug}`. It is where a voter confirms their own
ballot was counted and where anyone, with no special tools, can recount the
vote and check that the site matches this repository.

**Status:** built. The page follows this design; the commit shown is the one
the deploy workflow passes in when it builds the voting site. `docs/anonymity-and-security.md` lists what an
auditor should check in the code.

## What it shows

**While the election is Open** (read only, the same facts turnout already
makes public):

- Codes issued, codes used, ballots counted. Used must equal counted.
- Election settings: seats per contest, write-ins allowed, random order,
  live results on or off.
- Nothing else. No ballots, no receipt list and no per-choice counts, even
  if live results are on. Those stay on the election page, which is subject
  to the live-results setting.

**Once the election is Closed:**

1. **Audit numbers.** Codes issued, codes used, unused codes deleted,
   ballots counted, and a plain pass or fail on "used equals counted".
2. **Result per contest.** Winners, or **Tie: revote needed** with the tied
   choices and the seats still open.
3. **The receipt list.** Every receipt code, with a box to find yours. The
   search runs in the browser. The code you type is never sent to the server.
4. **Every anonymous ballot.** The choices on each ballot, including
   write-ins. Download as one JSON file and one CSV file.
5. **Recount in your browser.** A button that adds up the downloaded ballots
   with the same counting rules (including write-in normalization and the
   tie check) and compares the totals to the published result. A mismatch is
   shown in red.
6. **What is running.** The commit the site was built from, with a link to
   the repository at that commit and to the workflow run that deployed it.

## Rules

- The ballot list is published in receipt-code order. Receipt codes are
  random, so that order says nothing about when anyone voted. It is never
  storage order and never sorted by anything that could follow voting order.
- A ballot has no time and no sequence number, and neither does its row on
  this page or in the downloads.
- Election-level times (opened, closed) are fine to show. They describe the
  election, not a ballot.
- Nothing on the page is generated per visitor. Everyone sees the same
  files, so the page and downloads can be cached and mirrored.
- The audit page reads only published data and has no sign-in. Its database
  access is read only on closed elections, plus the three counts above while
  Open.
- Demo elections have an audit page too (`/audit/demo`) and work the same
  way.

## Why this matters

The audit page is the "anyone can check" promise made concrete. A voter can
check their ballot, a teacher can recount without trusting the site, and
someone technical can compare the running build to the code on GitHub. Where
that is not enough is listed in `docs/anonymity-and-security.md`, section
"What this does not protect against".

## The signed record (PDF)

When a vote is closed, the results page and the audit page offer **Download the
record (PDF) to print and sign**. It is one or two pages: the result of every
question (winners, or the tie and the seats that go to a revote), the audit
numbers with the pass or fail check, how to check it, and lines for the board
chair and two witnesses to sign. It holds nothing that is not already public.
The signatures are ink on the printed page; the site does not collect them.

The record carries a **fingerprint of the published ballot list**, and the audit
page shows the same value, so a signed copy can be matched to the ballots. To
recompute it from the JSON download: take the ballots in receipt-code order (plain
character-code order); write each as the receipt, a colon, then its lines
separated by commas, followed by a line feed. A line is the contest id with its
dashes removed, a slash, then the choice id with its dashes removed, or `w:` and
the write-in name. Sort a ballot's lines in plain character-code order. The
fingerprint is the lowercase hex SHA-256 of that text as UTF-8. The code is
`src/ChisBallot.Contracts/BallotFingerprint.cs`.

The PDF is drawn by a small writer in `src/ChisBallot.Data/PdfWriter.cs` with the
standard Helvetica and Courier fonts, so no library or font is needed on the
server. Letters outside the Windows-1252 set (for example Chinese names) print as
question marks on the record; the audit page and the JSON show them exactly.
