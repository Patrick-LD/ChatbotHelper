<script setup lang="ts">
import BaseButton from '@/components/BaseButton.vue'
import SourcesList from '@/components/SourcesList.vue'
import type { Message } from '@/types/chat'

defineProps<{ message: Message; canRetry?: boolean }>()
const emit = defineEmits<{ retry: [] }>()
</script>

<template>
  <article
    class="bubble"
    :class="`bubble--${message.role}`"
    :role="message.role === 'error' ? 'alert' : undefined"
    :aria-label="message.role === 'user' ? 'Din besked' : message.role === 'assistant' ? 'Svar fra assistenten' : 'Fejl'"
  >
    <template v-if="message.role === 'error'">
      <p class="bubble__title">{{ message.error?.title ?? 'Noget gik galt' }}</p>
      <p v-if="message.error?.detail" class="bubble__text">{{ message.error.detail }}</p>
      <BaseButton v-if="canRetry" variant="outline" class="bubble__retry" @click="emit('retry')">Prøv igen</BaseButton>
    </template>
    <template v-else>
      <p class="bubble__text">{{ message.text }}</p>
      <SourcesList v-if="message.sources?.length" :sources="message.sources" />
    </template>
  </article>
</template>

<style scoped>
.bubble {
  max-width: min(78%, 640px);
  padding: 14px 18px;
  border-radius: var(--radius-card);
  box-shadow: var(--shadow-md);
  overflow-wrap: anywhere;
}

.bubble__text {
  margin: 0;
  white-space: pre-wrap;
}

.bubble--user {
  align-self: flex-end;
  background: var(--color-secondary-1);
  color: var(--color-text-inverse);
  border-bottom-right-radius: 6px;
}

.bubble--assistant {
  align-self: flex-start;
  background: var(--color-bg);
  border-bottom-left-radius: 6px;
}

.bubble--error {
  align-self: stretch;
  max-width: none;
  background: var(--color-secondary-3);
  border-left: 6px solid var(--color-danger);
}

.bubble__title {
  margin: 0 0 4px;
  font-weight: 700;
}

.bubble__retry {
  margin-top: 12px;
}
</style>
