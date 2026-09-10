# Model Reasoning Capability Configuration

> Last updated: 2026-09-10

This reference describes the `models.*.reasoning` capability declaration in
`~/.insighta/config.json`.

It is a deployment capability record, not a session preference. The current CLI
does not yet provide a top-level `reasoning_preference` setting or a `/thinking`
command. Normal main-agent chat therefore keeps the model default behavior. The
mapping exists so a host can resolve an explicit product preference safely, and
so future CLI controls do not need adapter-specific configuration.

## Product preferences

Only these stable preference names may appear in `supported` or `mappings`:

| Preference | Meaning |
| --- | --- |
| `default` | Send no reasoning-control field; use the provider/model default. |
| `fast` | Prefer lower reasoning cost and latency. |
| `off` | Explicitly request no thinking, when the exact deployment supports it. |
| `balance` | Prefer a balanced reasoning setting. |
| `deep` | Prefer more thorough reasoning. |

They express user intent. They are not a claim that every provider offers an
identical reasoning budget, latency, or quality level.

## Built-in capability matrix

The packaged catalog is deliberately small and is matched by exact adapter and
model ID, case-insensitively.

| Adapter | Model ID | Available preferences | Strategy |
| --- | --- | --- | --- |
| `openai` | `gpt-5.1`, `gpt-5.2` | `default`, `fast`, `off`, `balance`, `deep` | `effort` |
| `openai-response` | `gpt-5.1`, `gpt-5.2` | `default`, `fast`, `off`, `balance`, `deep` | `effort` |
| `gemini` | `gemini-2.5-flash`, `gemini-2.5-flash-lite` | `default`, `off` | `none` |
| `anthropic` | `claude-sonnet-4-5`, `claude-sonnet-4-20250514` | `default`, `off` | `none` |

For every other model or deployment, **only `default` is available**. An
explicit non-default preference must fail instead of guessing a provider
protocol or silently falling back.

`fast`, `balance`, and `deep` are therefore currently available only for the
four listed OpenAI entries. A custom deployment can declare its own capability
record after its wire protocol has been verified.

## Complete override semantics

Put `reasoning` under the configured model entry:

```json
{
  "models": {
    "openai/gpt-5.2": {
      "model_id": "gpt-5.2",
      "reasoning": {
        "strategy": "effort",
        "supported": ["default", "fast", "off", "balance", "deep"],
        "mappings": {
          "fast": { "control": "effort", "effort": "minimal" },
          "off": { "control": "off", "offMode": "effortNone" },
          "balance": { "control": "effort", "effort": "medium" },
          "deep": { "control": "effort", "effort": "xHigh" }
        }
      }
    }
  }
}
```

This record **completely replaces** the packaged record for that configured
deployment. It is not merged preference by preference. If an override omits
`off`, for example, that deployment no longer declares `off` support.

The model entry is selected by its configuration key (for example
`openai/gpt-5.2`). The built-in fallback is selected using the configured
provider adapter and the entry's `model_id`.

## Schema and validation rules

| Field | Required | Rules |
| --- | --- | --- |
| `strategy` | Explicit for every record with an intensity mapping | One of `none`, `effort`, `budget`. Omitting it is equivalent to `none` and is permitted only for a `default`/`off`-only record. A record with `fast`, `balance`, or `deep` must declare `effort` or `budget` explicitly. |
| `supported` | Yes | Must include `default`. May contain only the five product preferences above. |
| `mappings` | Yes when a non-default preference is supported | Every supported non-default preference needs one mapping; mappings for unsupported preferences are invalid. `default` must never have a mapping. |
| `mappings.off` | When `off` is supported | Must use `control: "off"` and an adapter-compatible `offMode`. |
| Other mappings | When `fast`, `balance`, or `deep` is supported | Must use the control required by `strategy`: `effort` for `effort`, `budget` for `budget`. `strategy: "none"` cannot declare them. |

The supported native control fields are:

```json
{ "control": "effort", "effort": "minimal" }
{ "control": "budget", "budgetTokens": 4096 }
{ "control": "off", "offMode": "thinkingDisabled" }
```

`effort` accepts `none`, `minimal`, `low`, `medium`, `high`, or `xHigh` at the
shared request-model level. A concrete adapter may accept a smaller set. For
example, Gemini currently accepts only `minimal`, `low`, `medium`, and `high`
as `thinkingLevel`; unsupported values are rejected.

For `off`, use a protocol verified for the configured adapter:

| Adapter | Valid `offMode` values |
| --- | --- |
| `openai` | `effortNone`, `thinkingDisabled`, `enableThinkingFalse` |
| `openai-response` | `effortNone` |
| `anthropic` | `thinkingDisabled` |
| `gemini` | `thinkingBudgetZero` |

Do not configure an `effort` or `budget` mapping for an adapter until the exact
model/deployment protocol has been verified. In particular, Anthropic budget
handling remains subject to the `budget_tokens < max_tokens` decision recorded
in [TODO #21](TODO.md).

## Canonical JSON names

Use the JSON names shown in this document exactly:

```text
reasoning, strategy, supported, mappings,
control, effort, budgetTokens, offMode
```

The current reader accepts case-only variants, but hyphenated or invented names
such as `off-mode`, `budget-tokens`, or `thinking_level` are not part of this
schema. Unknown JSON properties are ignored by the current configuration reader
and can be lost when the CLI later writes `config.json`; do not rely on them for
reasoning controls.

For the surrounding model entry, use the existing names `model_id`,
`max_tokens`, and `context_window`.

## Safe rollout checklist

1. Confirm the exact adapter, model/deployment ID, and provider request schema.
2. Start with `default` and, where verified, `off`.
3. Choose exactly one `strategy` for all non-off mappings.
4. Add every desired non-default preference explicitly; overrides do not inherit
   omitted mappings from the packaged catalog.
5. Start a CLI chat with the deployment before using it for automated work; the
   resolver validates the capability record when it creates the LLM client.
