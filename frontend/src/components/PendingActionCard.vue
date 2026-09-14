<script setup lang="ts">
import BaseButton from '@/components/BaseButton.vue'

defineProps<{
  /** Opsummeringen fra API'et, f.eks. "Opret Mette Nielsen (…) som Konsulent i Salg med start 2026-11-01". */
  summary: string
  busy?: boolean
}>()

const emit = defineEmits<{ confirm: []; reject: [] }>()
</script>

<template>
  <section class="pending card" aria-labelledby="pending-title" role="region">
    <p id="pending-title" class="pending__title">Bekræft handling</p>
    <p class="pending__summary">{{ summary }}</p>
    <p class="pending__hint">
      Intet er udført endnu. Bekræft, annullér — eller skriv en rettelse i feltet nedenfor.
    </p>
    <div class="pending__actions">
      <BaseButton variant="primary" :disabled="busy" @click="emit('confirm')">Ja, udfør</BaseButton>
      <BaseButton variant="outline" :disabled="busy" @click="emit('reject')">Nej, annullér</BaseButton>
    </div>
  </section>
</template>

<style scoped>
.pending {
  padding: 20px 24px;
  background: var(--color-secondary-2);
  border-left: 6px solid var(--color-secondary-1);
  box-shadow: var(--shadow-md);
}

.pending__title {
  margin: 0 0 6px;
  font-size: 13px;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--color-secondary-1);
}

.pending__summary {
  margin: 0 0 6px;
  font-size: 17px;
  font-weight: 700;
}

.pending__hint {
  margin: 0 0 16px;
  font-size: 14px;
  color: var(--color-muted);
}

.pending__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
}
</style>
