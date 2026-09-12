# Weymela V3 UX and Design Standards (authoritative)

This document is permanent product guidance for all future Web/PWA phases. It applies to every rendered Business, Creator, Customer, Cashier, and Admin screen. Phase 2 must not implement Web UI; its DTOs and application contracts should provide clear data for a later compliant interface.

## User-facing language

Never expose raw domain or accounting identifiers. Use product language:

| Internal/domain | Business-facing | Creator-facing |
|---|---|---|
| `CreatorAllocation` | Creator Budget | Your Budget |
| `OriginalAllocation` | Starting Budget | Starting Budget |
| `RemainingAllocation` | Budget Remaining | Budget Remaining |
| `UnallocatedBudget` | Available Campaign Budget | — |
| `Promotion` | Campaign | Campaign |

Prefer Campaign, Campaign Budget, Active Campaigns, Join Campaign, Creator Budget, and Budget Remaining. Admin operational views may use precise terms such as Creator Allocation and Unallocated Budget when necessary.

## Interaction and visual stability

Buttons must remain visually stable. Do not use purple or dramatic hover changes, scaling/bouncing, glow effects, or unexpected animation. Subtle opacity, background, or border changes are acceptable. Every action needs a clear keyboard focus state and an understandable disabled/loading state. Primary actions use the consistent Weymela visual language and hierarchy.

## Mobile-first responsive design

Design mobile layouts intentionally first at 375, 390, 393, and 430px, then tablet and desktop at 1366×768, 1440×900, and 1920×1080. Every major screen must have no horizontal overflow. Dense desktop tables become cards, grouped sections, 2×2 metric grids, or bottom sheets/modals on phones. Controls remain touch-friendly; data must never collapse into long rows, single-letter wrapping, compressed buttons, or awkward scrolling.

Desktop may use aligned cards and structured tables with constrained content width, consistent columns, grouped actions, and intentional whitespace. Mobile and desktop are separate layout decisions, not a desktop shrink operation.

## Designed functionality

New functionality must be placed within the existing screen hierarchy. Before adding a field, button, row, or column: inspect the complete screen, select the correct section, group related information, use concise labels, preserve spacing, and verify both mobile and desktop rendering. Functionality must feel native rather than appended.

## Dashboard standard

Dashboards use meaningful grouped sections. A Business dashboard, for example, contains an Advertising Funds section with Total Balance, Available, and Reserved cards, followed by Campaigns, Creator Requests, Sales, and Pricing sections. Avoid a bare “Dashboard” heading followed by unrelated accounting rows.

## Table standard

Use a table only when it improves comprehension. Desktop operational data may use tables. On mobile, convert dense tables into responsive cards or grouped layouts; never force a narrow phone to render a desktop table.

## Visual acceptance gate

UI completion requires rendered browser inspection, not only TypeScript compilation, component existence, text assertions, or API correctness. Acceptance must cover the target mobile and desktop widths, no-overflow checks, touch controls, focus states, loading/disabled states, hierarchy, spacing, and the complete user flow. Screenshots or equivalent browser evidence must be retained with the acceptance record.
