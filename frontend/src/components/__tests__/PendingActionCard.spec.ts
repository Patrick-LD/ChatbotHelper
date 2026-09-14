import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import PendingActionCard from '../PendingActionCard.vue'

describe('PendingActionCard', () => {
  it('viser opsummeringen og sender confirm/reject', async () => {
    const wrapper = mount(PendingActionCard, { props: { summary: 'Opret Mette Nielsen (mette@firma.dk)' } })

    expect(wrapper.text()).toContain('Opret Mette Nielsen (mette@firma.dk)')
    expect(wrapper.text()).toContain('Intet er udført endnu')

    const [confirm, reject] = wrapper.findAll('button')
    await confirm!.trigger('click')
    await reject!.trigger('click')

    expect(wrapper.emitted('confirm')).toHaveLength(1)
    expect(wrapper.emitted('reject')).toHaveLength(1)
  })

  it('deaktiverer knapperne, mens der sendes', () => {
    const wrapper = mount(PendingActionCard, { props: { summary: 'Opret X', busy: true } })
    for (const button of wrapper.findAll('button')) {
      expect(button.attributes('disabled')).toBeDefined()
    }
  })
})
