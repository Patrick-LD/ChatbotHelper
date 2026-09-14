<script setup lang="ts">
import { onMounted, ref } from 'vue'
import MessageComposer from '@/components/MessageComposer.vue'
import MessageList from '@/components/MessageList.vue'
import PendingActionCard from '@/components/PendingActionCard.vue'
import { useAuthStore } from '@/stores/auth'
import { useChatStore } from '@/stores/chat'

const auth = useAuthStore()
const chat = useChatStore()
const composer = ref<InstanceType<typeof MessageComposer> | null>(null)

const suggestions = [
  'Hvor mange feriedage har jeg om året?',
  'Hvordan opretter jeg en ny medarbejder?',
  'Hvad gør jeg, hvis jeg har klikket på et phishing-link?',
]

onMounted(async () => {
  // Efter reload er nøglen i sessionStorage, men navn/roller skal hentes igen.
  await auth.restore()
  composer.value?.focus()
})

function ask(text: string) {
  void chat.send(text)
}
</script>

<template>
  <div class="chat container">
    <section class="chat__panel card" aria-label="Samtale">
      <div v-if="!chat.hasMessages" class="chat__empty">
        <h1 class="display chat__heading">Hvad kan jeg hjælpe med?</h1>
        <p class="chat__lead">
          Spørg om interne regler, systemer og processer — eller bed mig om at udføre en opgave.
          Handlinger udføres først, når du har bekræftet dem.
        </p>
        <ul class="chat__suggestions" aria-label="Forslag">
          <li v-for="suggestion in suggestions" :key="suggestion">
            <button type="button" class="chat__chip" @click="ask(suggestion)">{{ suggestion }}</button>
          </li>
        </ul>
      </div>

      <MessageList
        v-else
        :messages="chat.messages"
        :is-sending="chat.isSending"
        :can-retry="chat.canRetry"
        @retry="chat.retry()"
      />

      <div v-if="chat.pendingAction && !chat.isSending" class="chat__pending">
        <PendingActionCard
          :summary="chat.pendingAction"
          :busy="chat.isSending"
          @confirm="chat.confirm()"
          @reject="chat.reject()"
        />
      </div>

      <MessageComposer ref="composer" :disabled="chat.isSending" @send="ask" />
    </section>
  </div>
</template>

<style scoped>
.chat {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
  padding-block: 24px;
  max-width: var(--chat-width);
}

.chat__panel {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
  height: calc(100dvh - 72px - 48px);
  overflow: hidden;
}

.chat__empty {
  flex: 1;
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
  text-align: center;
  padding: 40px 24px;
  overflow-y: auto;
}

.chat__heading {
  font-size: clamp(32px, 5vw, 48px);
  margin-bottom: 12px;
}

.chat__lead {
  max-width: 520px;
  color: var(--color-muted);
  margin-bottom: 28px;
}

.chat__suggestions {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 10px;
}

.chat__chip {
  padding: 10px 18px;
  border: 2px solid var(--color-secondary-1);
  border-radius: var(--radius-pill);
  background: transparent;
  cursor: pointer;
  font-size: 14px;
  font-weight: 700;
  transition:
    background-color var(--transition),
    color var(--transition);
}

.chat__chip:hover {
  background: var(--color-primary);
  border-color: var(--color-primary);
}

.chat__pending {
  padding: 0 20px 12px;
}

@media (max-width: 640px) {
  .chat {
    padding-block: 0;
    padding-inline: 0;
  }

  .chat__panel {
    height: calc(100dvh - 72px);
    border-radius: 0;
    box-shadow: none;
  }
}
</style>
