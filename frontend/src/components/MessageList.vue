<script setup lang="ts">
import { nextTick, ref, watch } from 'vue'
import MessageBubble from '@/components/MessageBubble.vue'
import TypingIndicator from '@/components/TypingIndicator.vue'
import type { Message } from '@/types/chat'

const props = defineProps<{ messages: Message[]; isSending: boolean; canRetry: boolean }>()
const emit = defineEmits<{ retry: [] }>()

const container = ref<HTMLElement | null>(null)

// Ruller til bunden, når der kommer en ny boble eller "skriver…"-indikatoren tændes.
watch(
  () => [props.messages.length, props.isSending] as const,
  async () => {
    await nextTick()
    container.value?.scrollTo({ top: container.value.scrollHeight, behavior: 'smooth' })
  },
)
</script>

<template>
  <div ref="container" class="list" aria-live="polite" aria-relevant="additions">
    <MessageBubble
      v-for="(message, index) in messages"
      :key="message.id"
      :message="message"
      :can-retry="canRetry && index === messages.length - 1"
      @retry="emit('retry')"
    />
    <TypingIndicator v-if="isSending" class="list__typing" />
  </div>
</template>

<style scoped>
.list {
  display: flex;
  flex-direction: column;
  gap: 14px;
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 24px 20px;
  scroll-behavior: smooth;
}

.list__typing {
  align-self: flex-start;
}
</style>
