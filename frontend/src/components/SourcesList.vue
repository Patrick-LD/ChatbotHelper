<script setup lang="ts">
import type { SourceDto } from '@/types/api'

defineProps<{ sources: SourceDto[] }>()

/** "personalehaandbog.md" → "personalehaandbog" — filendelsen siger ikke brugeren noget. */
function documentName(source: string): string {
  return source.replace(/\.(md|txt)$/i, '')
}

function formatScore(score: number): string {
  return score.toLocaleString('da-DK', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}
</script>

<template>
  <details v-if="sources.length" class="sources">
    <summary class="sources__summary">Kilder ({{ sources.length }})</summary>
    <ul class="sources__list">
      <li v-for="(source, index) in sources" :key="`${source.source}-${source.heading}-${index}`" class="sources__item">
        <span class="sources__doc">{{ documentName(source.source) }}</span>
        <span class="sources__sep" aria-hidden="true">›</span>
        <span class="sources__heading">{{ source.heading }}</span>
        <span class="sources__score" :title="`Lighed ${formatScore(source.score)}`">{{ formatScore(source.score) }}</span>
      </li>
    </ul>
  </details>
</template>

<style scoped>
.sources {
  margin-top: 10px;
  font-size: 13px;
}

.sources__summary {
  cursor: pointer;
  color: var(--color-muted);
  font-weight: 700;
}

.sources__list {
  list-style: none;
  margin: 8px 0 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.sources__item {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 6px;
  padding: 6px 12px;
  border-radius: var(--radius-sm);
  background: var(--color-secondary-2);
}

.sources__doc {
  font-weight: 700;
}

.sources__sep,
.sources__score {
  color: var(--color-muted);
}

.sources__score {
  margin-left: auto;
  font-variant-numeric: tabular-nums;
}
</style>
