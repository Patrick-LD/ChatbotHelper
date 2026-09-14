import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import MessageComposer from '../MessageComposer.vue'

describe('MessageComposer', () => {
  it('Enter sender teksten og tømmer feltet; Shift+Enter gør ikke', async () => {
    const wrapper = mount(MessageComposer)
    const textarea = wrapper.find('textarea')

    await textarea.setValue('Hej bot')
    await textarea.trigger('keydown', { key: 'Enter', shiftKey: true })
    expect(wrapper.emitted('send')).toBeUndefined()

    await textarea.trigger('keydown', { key: 'Enter' })
    expect(wrapper.emitted('send')).toEqual([['Hej bot']])
    expect((textarea.element as HTMLTextAreaElement).value).toBe('')
  })

  it('sender ikke tom tekst og er låst, når disabled', async () => {
    const wrapper = mount(MessageComposer, { props: { disabled: true } })
    const textarea = wrapper.find('textarea')

    await textarea.setValue('   ')
    await wrapper.find('form').trigger('submit')
    expect(wrapper.emitted('send')).toBeUndefined()
    expect(textarea.attributes('disabled')).toBeDefined()
    expect(wrapper.find('button').attributes('disabled')).toBeDefined()
  })
})
